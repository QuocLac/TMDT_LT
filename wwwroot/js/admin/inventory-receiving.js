(() => {
    'use strict';

    const api = '/Admin/Inventory/Receiving';
    const state = {
        bootstrap: null,
        products: [],
        lines: new Map(),
        page: 1,
        totalPages: 1,
        totalItems: 0,
        checked: false,
        checkedFingerprint: '',
        selectedQuickProduct: null,
        requestSequence: 0
    };

    const $ = id => document.getElementById(id);
    const money = value => `${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 }).format(Number(value || 0))} đ`;
    const number = value => new Intl.NumberFormat('vi-VN').format(Number(value || 0));
    const token = () => document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';

    function escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    function debounce(fn, delay = 300) {
        let timer;
        return (...args) => {
            clearTimeout(timer);
            timer = setTimeout(() => fn(...args), delay);
        };
    }

    async function readJson(response) {
        const data = await response.json().catch(() => ({ success: false, message: 'Phản hồi từ máy chủ không hợp lệ.' }));
        if (!response.ok || data.success === false) {
            throw new Error(data.message || `Yêu cầu không thành công (HTTP ${response.status}).`);
        }
        return data;
    }

    async function getJson(url) {
        return readJson(await fetch(url, { headers: { Accept: 'application/json' } }));
    }

    async function postJson(url, payload) {
        return readJson(await fetch(url, {
            method: 'POST',
            headers: {
                Accept: 'application/json',
                'Content-Type': 'application/json',
                RequestVerificationToken: token()
            },
            body: JSON.stringify(payload)
        }));
    }

    async function postForm(url, formData) {
        return readJson(await fetch(url, {
            method: 'POST',
            headers: {
                Accept: 'application/json',
                RequestVerificationToken: token()
            },
            body: formData
        }));
    }

    function showPageAlert(message) {
        const box = $('receivingPageAlert');
        box.textContent = message || '';
        box.hidden = !message;
    }

    function setToday() {
        const now = new Date();
        const localDate = new Date(now.getTime() - now.getTimezoneOffset() * 60000).toISOString().slice(0, 10);
        $('invoiceDate').value = localDate;
        $('receivedDate').value = localDate;
        $('invoiceDate').max = localDate;
        $('receivedDate').max = localDate;
    }

    function fillSelect(id, items, valueKey, labelBuilder, placeholder) {
        const select = $(id);
        select.innerHTML = [
            `<option value="">${escapeHtml(placeholder)}</option>`,
            ...items.map(item => `<option value="${escapeHtml(item[valueKey])}">${escapeHtml(labelBuilder(item))}</option>`)
        ].join('');
    }

    async function loadBootstrap() {
        const data = await getJson(`${api}/Bootstrap`);
        state.bootstrap = data;
        fillSelect('supplierId', data.suppliers || [], 'supplierId', item => item.taxCode ? `${item.supplierName} · MST ${item.taxCode}` : item.supplierName, 'Chọn nhà cung cấp');
        fillSelect('warehouseId', data.warehouses || [], 'warehouseId', item => `${item.warehouseCode} · ${item.warehouseName}`, 'Chọn kho nhận');
        fillSelect('categoryFilter', data.categories || [], 'categoryId', item => item.categoryName, 'Tất cả danh mục');
        fillSelect('brandFilter', data.brands || [], 'brandId', item => item.brandName, 'Tất cả thương hiệu');
        fillSelect('quickCategoryId', data.categories || [], 'categoryId', item => item.categoryName, 'Chọn danh mục');
        fillSelect('quickBrandId', data.brands || [], 'brandId', item => item.brandName, 'Chọn thương hiệu');

        const primary = (data.warehouses || []).find(item => item.isPrimary) || data.warehouses?.[0];
        if (primary) $('warehouseId').value = String(primary.warehouseId);
        updateSupplierMeta();
        updateWarehouseMeta(false);
    }

    function updateSupplierMeta() {
        const id = Number($('supplierId').value || 0);
        const item = state.bootstrap?.suppliers?.find(value => Number(value.supplierId) === id);
        $('supplierMeta').textContent = item
            ? [item.contactName, item.phone, item.taxCode && `MST ${item.taxCode}`].filter(Boolean).join(' · ') || 'Đã chọn nhà cung cấp.'
            : 'Chọn nhà cung cấp phát hành chứng từ.';
        invalidateReceipt();
    }

    function updateWarehouseMeta(reload = true) {
        const id = Number($('warehouseId').value || 0);
        const item = state.bootstrap?.warehouses?.find(value => Number(value.warehouseId) === id);
        $('warehouseMeta').textContent = item ? `${item.warehouseCode} · ${item.address || 'Chưa có địa chỉ'}` : 'Chọn kho nhận hàng.';
        invalidateReceipt();
        if (reload) loadProducts(true);
    }

    function productQuery() {
        const query = new URLSearchParams({
            warehouseId: $('warehouseId').value,
            q: $('productSearch').value.trim(),
            categoryId: $('categoryFilter').value,
            brandId: $('brandFilter').value,
            stockScope: $('stockFilter').value,
            page: String(state.page),
            pageSize: '12'
        });
        return query;
    }

    async function loadProducts(reset = false) {
        if (reset) state.page = 1;
        const grid = $('productGrid');
        if (!Number($('warehouseId').value || 0)) {
            grid.innerHTML = '<div class="inventory-empty">Hãy chọn kho nhận để tải mặt hàng.</div>';
            return;
        }
        const sequence = ++state.requestSequence;
        grid.innerHTML = '<div class="inventory-empty">Đang tải mặt hàng...</div>';
        try {
            const data = await getJson(`${api}/Products?${productQuery()}`);
            if (sequence !== state.requestSequence) return;
            state.products = data.items || [];
            state.page = data.page || 1;
            state.totalPages = data.totalPages || 1;
            state.totalItems = data.totalItems || 0;
            $('productResultCount').textContent = `${number(state.totalItems)} mặt hàng`;
            $('pageIndicator').textContent = `Trang ${state.page} / ${state.totalPages}`;
            $('previousPageButton').disabled = state.page <= 1;
            $('nextPageButton').disabled = state.page >= state.totalPages;
            grid.innerHTML = state.products.length ? state.products.map(item => {
                const selected = state.lines.has(Number(item.variantId));
                return `
                    <article class="inventory-product-card ${selected ? 'is-selected' : ''}">
                        <img src="${escapeHtml(item.imageUrl)}" alt="${escapeHtml(item.productName)}" onerror="this.src='/images/products/default-product.png'" />
                        <div class="inventory-product-card-body">
                            <strong>${escapeHtml(item.productName)}</strong>
                            <small>${escapeHtml(item.sku)} · ${escapeHtml(variantLabel(item))}</small>
                            <small>${escapeHtml(item.brandName)} · ${escapeHtml(item.categoryName)}</small>
                        </div>
                        <div class="inventory-product-card-foot">
                            <span>Tồn kho <strong>${number(item.stock)}</strong></span>
                            <button type="button" data-add-product="${item.variantId}">${selected ? 'Đã thêm' : 'Thêm'}</button>
                        </div>
                    </article>`;
            }).join('') : '<div class="inventory-empty">Không tìm thấy mặt hàng phù hợp.</div>';
        } catch (error) {
            if (sequence === state.requestSequence) {
                grid.innerHTML = `<div class="inventory-empty">${escapeHtml(error.message)}</div>`;
            }
        }
    }

    function variantLabel(item) {
        return [item.color, item.storage, item.ram].filter(Boolean).join(' / ') || item.variantLabel || 'Mặc định';
    }

    function addProduct(variantId) {
        const product = state.products.find(item => Number(item.variantId) === Number(variantId));
        if (!product) return;
        if (state.lines.has(Number(variantId))) {
            const line = state.lines.get(Number(variantId));
            line.quantity += 1;
        } else {
            state.lines.set(Number(variantId), {
                ...product,
                variantLabel: variantLabel(product),
                quantity: 1,
                importPrice: Number(product.lastImportPrice || product.currentCostPrice || 0),
                taxRate: 0.1
            });
        }
        renderLines();
        invalidateReceipt();
        loadProducts();
    }

    function addQuickItem(result) {
        state.lines.set(Number(result.variantId), {
            variantId: Number(result.variantId),
            productName: result.productName,
            sku: result.sku,
            variantLabel: result.variantLabel,
            imageUrl: result.imageUrl || '/images/products/default-product.png',
            stock: 0,
            quantity: 1,
            importPrice: Number(result.initialImportPrice || 0),
            taxRate: 0.1
        });
        renderLines();
        invalidateReceipt();
        closeModal('quickItemModal');
        resetQuickForm();
        loadProducts(true);
    }

    function renderLines() {
        const lines = [...state.lines.values()];
        $('selectedLineCount').textContent = lines.length ? `${lines.length} mặt hàng` : 'Chưa chọn mặt hàng';
        const body = $('receiptLineBody');
        if (!lines.length) {
            body.innerHTML = '<tr><td colspan="6" class="inventory-empty">Chưa có mặt hàng trong phiếu.</td></tr>';
            updateLocalSummary();
            return;
        }
        body.innerHTML = lines.map(item => `
            <tr>
                <td><div class="inventory-product-cell"><img src="${escapeHtml(item.imageUrl)}" onerror="this.src='/images/products/default-product.png'" alt=""><div><strong>${escapeHtml(item.productName)}</strong><small>${escapeHtml(item.sku)} · ${escapeHtml(item.variantLabel)}</small></div></div></td>
                <td><input class="inventory-table-input" type="number" min="1" max="10000" value="${item.quantity}" data-line-field="quantity" data-id="${item.variantId}"></td>
                <td><input class="inventory-table-input" type="number" min="1" step="1000" value="${item.importPrice}" data-line-field="importPrice" data-id="${item.variantId}"></td>
                <td><select class="inventory-table-input" data-line-field="taxRate" data-id="${item.variantId}"><option value="0" ${item.taxRate === 0 ? 'selected' : ''}>0%</option><option value="0.08" ${item.taxRate === 0.08 ? 'selected' : ''}>8%</option><option value="0.1" ${item.taxRate === 0.1 ? 'selected' : ''}>10%</option></select></td>
                <td class="is-number">${money(item.quantity * item.importPrice)}</td>
                <td><button class="inventory-remove-button" type="button" data-remove-line="${item.variantId}">Xóa</button></td>
            </tr>`).join('');
        updateLocalSummary();
    }

    function updateLocalSummary() {
        const lines = [...state.lines.values()];
        const goods = lines.reduce((sum, item) => sum + Number(item.quantity || 0) * Number(item.importPrice || 0), 0);
        const tax = lines.reduce((sum, item) => sum + Number(item.quantity || 0) * Number(item.importPrice || 0) * Number(item.taxRate || 0), 0);
        const shipping = Number($('shippingFee').value || 0);
        const other = Number($('otherFee').value || 0);
        $('goodsSubtotal').textContent = money(goods);
        $('inputVatAmount').textContent = money(tax);
        $('allocatedFeeTotal').textContent = money(shipping + other);
        $('supplierPayable').textContent = money(goods + tax + shipping + other);
        $('capitalizedCost').textContent = money(goods + shipping + other + ($('inputVatDeductible').checked ? 0 : tax));
    }

    function buildPayload() {
        return {
            supplierId: Number($('supplierId').value || 0),
            warehouseId: Number($('warehouseId').value || 0),
            invoiceNumber: $('invoiceNumber').value.trim(),
            invoiceDate: $('invoiceDate').value || null,
            receivedDate: $('receivedDate').value || null,
            shippingFee: Number($('shippingFee').value || 0),
            otherFee: Number($('otherFee').value || 0),
            inputVatDeductible: $('inputVatDeductible').checked,
            note: $('receiptNote').value.trim(),
            items: [...state.lines.values()].map(item => ({
                variantId: Number(item.variantId),
                quantity: Number(item.quantity),
                importPrice: Number(item.importPrice),
                taxRate: Number(item.taxRate)
            }))
        };
    }

    function fingerprint(payload) {
        return JSON.stringify(payload);
    }

    function validatePayload(payload) {
        if (!payload.supplierId) return 'Hãy chọn nhà cung cấp.';
        if (!payload.warehouseId) return 'Hãy chọn kho nhận.';
        if (!payload.invoiceNumber || payload.invoiceNumber.length < 3) return 'Hãy nhập số hóa đơn hoặc chứng từ hợp lệ.';
        if (!payload.invoiceDate || !payload.receivedDate) return 'Hãy chọn ngày chứng từ và ngày nhận hàng.';
        if (!payload.items.length) return 'Hãy thêm ít nhất một mặt hàng.';
        if (payload.items.some(item => item.quantity <= 0 || item.importPrice <= 0)) return 'Số lượng và giá nhập phải lớn hơn 0.';
        return '';
    }

    function invalidateReceipt() {
        state.checked = false;
        state.checkedFingerprint = '';
        $('submitButton').disabled = true;
        $('previewState').className = 'inventory-alert';
        $('previewState').textContent = state.lines.size ? 'Thông tin đã thay đổi. Hãy kiểm tra lại phiếu.' : 'Thêm mặt hàng để bắt đầu.';
        $('previewWarnings').innerHTML = '';
        updateLocalSummary();
    }

    async function checkReceipt() {
        const payload = buildPayload();
        const validation = validatePayload(payload);
        if (validation) {
            $('previewState').className = 'inventory-alert inventory-alert-error';
            $('previewState').textContent = validation;
            return;
        }
        const button = $('previewButton');
        button.disabled = true;
        try {
            const result = await postJson(`${api}/Preview`, payload);
            $('goodsSubtotal').textContent = money(result.goodsSubtotal);
            $('inputVatAmount').textContent = money(result.inputVatAmount);
            $('allocatedFeeTotal').textContent = money(Number(result.shippingFee) + Number(result.otherFee));
            $('supplierPayable').textContent = money(result.supplierPayable);
            $('capitalizedCost').textContent = money(result.inventoryCapitalizedCost);
            $('previewWarnings').innerHTML = (result.warnings || []).map(message => `<div class="inventory-alert inventory-alert-warning">${escapeHtml(message)}</div>`).join('');
            $('previewState').className = 'inventory-alert inventory-alert-success';
            $('previewState').textContent = 'Phiếu hợp lệ. Có thể xác nhận nhập kho.';
            state.checked = true;
            state.checkedFingerprint = fingerprint(payload);
            $('submitButton').disabled = false;
        } catch (error) {
            $('previewState').className = 'inventory-alert inventory-alert-error';
            $('previewState').textContent = error.message;
        } finally {
            button.disabled = false;
        }
    }

    async function submitReceipt() {
        const payload = buildPayload();
        if (!state.checked || state.checkedFingerprint !== fingerprint(payload)) {
            invalidateReceipt();
            $('previewState').className = 'inventory-alert inventory-alert-error';
            $('previewState').textContent = 'Thông tin đã thay đổi. Hãy kiểm tra lại phiếu.';
            return;
        }
        if (!confirm('Xác nhận nhập các mặt hàng vào kho?')) return;
        const button = $('submitButton');
        button.disabled = true;
        try {
            const result = await postJson(`${api}/Submit`, payload);
            $('resultModalMessage').textContent = result.message || 'Đã cập nhật kho.';
            $('resultModalDetail').innerHTML = `
                <div><span>Mã phiếu</span><strong>${escapeHtml(result.poCode)}</strong></div>
                <div><span>Số chứng từ</span><strong>${escapeHtml(result.invoiceNumber || '—')}</strong></div>
                <div><span>Số lượng nhận</span><strong>${number(result.totalQuantity)}</strong></div>
                <div><span>Tổng phải trả</span><strong>${money(result.supplierPayable)}</strong></div>
                <div><span>Giá trị nhập kho</span><strong>${money(result.inventoryCapitalizedCost)}</strong></div>`;
            openModal('resultModal');
            await loadRecent();
        } catch (error) {
            $('previewState').className = 'inventory-alert inventory-alert-error';
            $('previewState').textContent = error.message;
            button.disabled = false;
        }
    }

    async function loadRecent() {
        const container = $('recentReceiptList');
        container.innerHTML = '<div class="inventory-empty">Đang tải...</div>';
        try {
            const data = await getJson(`${api}/Recent?take=8`);
            const receipts = data.receipts || [];
            container.innerHTML = receipts.length ? receipts.map(item => `
                <div class="inventory-recent-item">
                    <div><strong>${escapeHtml(item.poCode)}</strong><small>${escapeHtml(item.supplierName)} · ${escapeHtml(item.warehouseName)}</small></div>
                    <div><strong>${money(item.totalAmount)}</strong><small>${item.orderDate ? new Date(item.orderDate).toLocaleDateString('vi-VN') : '—'}</small></div>
                </div>`).join('') : '<div class="inventory-empty">Chưa có phiếu nhập.</div>';
        } catch (error) {
            container.innerHTML = `<div class="inventory-empty">${escapeHtml(error.message)}</div>`;
        }
    }

    function openModal(id) {
        const modal = $(id);
        if (modal) modal.hidden = false;
    }

    function closeModal(id) {
        const modal = $(id);
        if (modal) modal.hidden = true;
    }

    function setQuickMode(mode) {
        const isNew = mode === 'new';
        $('newProductFields').hidden = !isNew;
        $('existingProductFields').hidden = isNew;
        $('quickItemError').hidden = true;
        if (!isNew) loadProductOptions('');
    }

    async function loadProductOptions(query) {
        const container = $('quickProductOptions');
        container.innerHTML = '<div class="inventory-empty">Đang tìm...</div>';
        try {
            const data = await getJson(`${api}/ProductOptions?q=${encodeURIComponent(query || '')}&take=20`);
            const products = data.products || [];
            container.innerHTML = products.length ? products.map(item => `
                <button type="button" class="inventory-option" data-select-quick-product="${item.productId}">
                    <img src="${escapeHtml(item.imageUrl || '/images/products/default-product.png')}" onerror="this.src='/images/products/default-product.png'" alt="" />
                    <span><strong>${escapeHtml(item.productName)}</strong><small>${escapeHtml(item.brandName)} · ${escapeHtml(item.categoryName)}</small></span>
                </button>`).join('') : '<div class="inventory-empty">Không tìm thấy sản phẩm.</div>';
            container._productData = products;
        } catch (error) {
            container.innerHTML = `<div class="inventory-empty">${escapeHtml(error.message)}</div>`;
        }
    }

    function selectQuickProduct(productId) {
        const products = $('quickProductOptions')._productData || [];
        const item = products.find(product => Number(product.productId) === Number(productId));
        if (!item) return;
        state.selectedQuickProduct = item;
        $('quickProductId').value = item.productId;
        $('selectedQuickProduct').hidden = false;
        $('selectedQuickProduct').innerHTML = `<strong>${escapeHtml(item.productName)}</strong><small>${escapeHtml(item.brandName)} · ${escapeHtml(item.categoryName)}</small>`;
        $('quickProductOptions').innerHTML = '';
        $('quickProductSearch').value = item.productName;
    }

    function quickFormData() {
        const mode = document.querySelector('input[name="quickItemMode"]:checked')?.value || 'new';
        const form = new FormData();
        form.append('CreateNewProduct', String(mode === 'new'));
        if (mode === 'new') {
            form.append('ProductName', $('quickProductName').value.trim());
            form.append('CategoryId', $('quickCategoryId').value);
            form.append('BrandId', $('quickBrandId').value);
        } else {
            form.append('ProductId', $('quickProductId').value);
        }
        form.append('Color', $('quickColor').value.trim());
        form.append('Storage', $('quickStorage').value.trim());
        form.append('Ram', $('quickRam').value.trim());
        form.append('ListPrice', $('quickListPrice').value);
        form.append('InitialImportPrice', $('quickImportPrice').value);
        form.append('ImageUrl', $('quickImageUrl').value.trim());
        const file = $('quickImageFile').files?.[0];
        if (file) form.append('ImageFile', file);
        return form;
    }

    function validateQuickForm() {
        const mode = document.querySelector('input[name="quickItemMode"]:checked')?.value || 'new';
        if (mode === 'new' && (!$('quickProductName').value.trim() || !$('quickCategoryId').value || !$('quickBrandId').value)) return 'Tên sản phẩm, danh mục và thương hiệu là bắt buộc.';
        if (mode === 'variant' && !$('quickProductId').value) return 'Hãy chọn sản phẩm cần thêm biến thể.';
        if (!$('quickColor').value.trim() || !$('quickStorage').value.trim()) return 'Màu sắc và cấu hình chính là bắt buộc.';
        if (Number($('quickListPrice').value || 0) <= 0 || Number($('quickImportPrice').value || 0) <= 0) return 'Giá bán và giá nhập phải lớn hơn 0.';
        const file = $('quickImageFile').files?.[0];
        if (file && file.size > 5 * 1024 * 1024) return 'Ảnh sản phẩm không được vượt quá 5 MB.';
        return '';
    }

    async function saveQuickItem(event) {
        event.preventDefault();
        const errorBox = $('quickItemError');
        const validation = validateQuickForm();
        if (validation) {
            errorBox.textContent = validation;
            errorBox.hidden = false;
            return;
        }
        const button = $('saveQuickItemButton');
        button.disabled = true;
        try {
            const result = await postForm(`${api}/QuickItem`, quickFormData());
            addQuickItem(result);
        } catch (error) {
            errorBox.textContent = error.message;
            errorBox.hidden = false;
        } finally {
            button.disabled = false;
        }
    }

    function resetQuickForm() {
        $('quickItemForm').reset();
        state.selectedQuickProduct = null;
        $('quickProductId').value = '';
        $('selectedQuickProduct').hidden = true;
        $('selectedQuickProduct').innerHTML = '';
        $('quickProductOptions').innerHTML = '';
        $('quickItemError').hidden = true;
        document.querySelector('input[name="quickItemMode"][value="new"]').checked = true;
        setQuickMode('new');
    }

    function resetReceipt() {
        state.lines.clear();
        $('invoiceNumber').value = '';
        $('receiptNote').value = '';
        $('shippingFee').value = '0';
        $('otherFee').value = '0';
        $('inputVatDeductible').checked = true;
        setToday();
        renderLines();
        invalidateReceipt();
        closeModal('resultModal');
        loadProducts(true);
    }

    function bindEvents() {
        $('supplierId').addEventListener('change', updateSupplierMeta);
        $('warehouseId').addEventListener('change', () => updateWarehouseMeta(true));
        $('productSearch').addEventListener('input', debounce(() => loadProducts(true)));
        ['categoryFilter', 'brandFilter', 'stockFilter'].forEach(id => $(id).addEventListener('change', () => loadProducts(true)));
        $('resetFilterButton').addEventListener('click', () => {
            $('productSearch').value = '';
            $('categoryFilter').value = '';
            $('brandFilter').value = '';
            $('stockFilter').value = 'all';
            loadProducts(true);
        });
        $('previousPageButton').addEventListener('click', () => { if (state.page > 1) { state.page--; loadProducts(); } });
        $('nextPageButton').addEventListener('click', () => { if (state.page < state.totalPages) { state.page++; loadProducts(); } });
        $('productGrid').addEventListener('click', event => { const button = event.target.closest('[data-add-product]'); if (button) addProduct(Number(button.dataset.addProduct)); });
        $('receiptLineBody').addEventListener('click', event => { const button = event.target.closest('[data-remove-line]'); if (!button) return; state.lines.delete(Number(button.dataset.removeLine)); renderLines(); invalidateReceipt(); loadProducts(); });
        $('receiptLineBody').addEventListener('change', event => {
            const field = event.target.closest('[data-line-field]'); if (!field) return;
            const line = state.lines.get(Number(field.dataset.id)); if (!line) return;
            let value = Number(field.value || 0);
            if (field.dataset.lineField === 'quantity') value = Math.max(1, Math.min(10000, value));
            if (field.dataset.lineField === 'importPrice') value = Math.max(1, value);
            line[field.dataset.lineField] = value;
            renderLines(); invalidateReceipt();
        });
        $('clearLinesButton').addEventListener('click', () => { state.lines.clear(); renderLines(); invalidateReceipt(); loadProducts(); });
        ['invoiceNumber', 'invoiceDate', 'receivedDate', 'receiptNote', 'shippingFee', 'otherFee'].forEach(id => $(id).addEventListener('input', invalidateReceipt));
        $('inputVatDeductible').addEventListener('change', invalidateReceipt);
        $('previewButton').addEventListener('click', checkReceipt);
        $('submitButton').addEventListener('click', submitReceipt);
        $('reloadRecentButton').addEventListener('click', loadRecent);
        $('createAnotherButton').addEventListener('click', resetReceipt);

        $('openQuickItemButton').addEventListener('click', () => { resetQuickForm(); openModal('quickItemModal'); });
        document.querySelectorAll('[data-close-modal]').forEach(button => button.addEventListener('click', () => closeModal(button.dataset.closeModal)));
        document.querySelectorAll('input[name="quickItemMode"]').forEach(input => input.addEventListener('change', () => setQuickMode(input.value)));
        $('quickProductSearch').addEventListener('input', debounce(() => { state.selectedQuickProduct = null; $('quickProductId').value = ''; $('selectedQuickProduct').hidden = true; loadProductOptions($('quickProductSearch').value.trim()); }, 250));
        $('quickProductOptions').addEventListener('click', event => { const button = event.target.closest('[data-select-quick-product]'); if (button) selectQuickProduct(Number(button.dataset.selectQuickProduct)); });
        $('quickItemForm').addEventListener('submit', saveQuickItem);
    }

    async function initialize() {
        setToday();
        bindEvents();
        renderLines();
        try {
            await loadBootstrap();
            await Promise.all([loadProducts(true), loadRecent()]);
        } catch (error) {
            showPageAlert(error.message);
        }
    }

    document.addEventListener('DOMContentLoaded', initialize);
})();
