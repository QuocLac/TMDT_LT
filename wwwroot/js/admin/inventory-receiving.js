(() => {
    'use strict';

    const state = {
        bootstrap: null,
        products: [],
        lines: new Map(),
        page: 1,
        pageSize: 12,
        totalPages: 1,
        totalItems: 0,
        loadingProducts: false,
        previewValid: false,
        previewFingerprint: '',
        requestSequence: 0
    };

    const money = new Intl.NumberFormat('vi-VN', {
        maximumFractionDigits: 0
    });

    const byId = id => document.getElementById(id);
    const value = id => byId(id)?.value?.trim() || '';
    const numberValue = id => Number.parseFloat(byId(id)?.value || '0') || 0;
    const integerValue = id => Number.parseInt(byId(id)?.value || '0', 10) || 0;
    const antiForgeryToken = () =>
        document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';

    function escapeHtml(input) {
        return String(input ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    function formatMoney(amount) {
        return `${money.format(Number(amount || 0))} đ`;
    }

    function setStatus(message, mode = '') {
        const element = byId('systemStatus');
        if (!element) return;
        element.classList.remove('is-ready', 'is-error');
        if (mode) element.classList.add(mode);
        element.innerHTML = `<span class="ir-status-dot"></span>${escapeHtml(message)}`;
    }

    function showPageWarning(message) {
        const element = byId('pageWarning');
        if (!element) return;
        element.textContent = message;
        element.hidden = !message;
    }

    async function getJson(url) {
        const response = await fetch(url, {
            headers: { 'Accept': 'application/json' }
        });
        const data = await response.json().catch(() => null);
        if (!response.ok || !data || data.success === false) {
            throw new Error(data?.message || `Không thể tải dữ liệu (HTTP ${response.status}).`);
        }
        return data;
    }

    async function postJson(url, payload) {
        const response = await fetch(url, {
            method: 'POST',
            headers: {
                'Accept': 'application/json',
                'Content-Type': 'application/json',
                'RequestVerificationToken': antiForgeryToken()
            },
            body: JSON.stringify(payload)
        });
        const data = await response.json().catch(() => null);
        if (!response.ok || !data || data.success === false) {
            throw new Error(data?.message || `Yêu cầu thất bại (HTTP ${response.status}).`);
        }
        return data;
    }

    function setTodayDefaults() {
        const local = new Date();
        const year = local.getFullYear();
        const month = String(local.getMonth() + 1).padStart(2, '0');
        const day = String(local.getDate()).padStart(2, '0');
        const today = `${year}-${month}-${day}`;
        byId('invoiceDate').value = today;
        byId('receivedDate').value = today;
        byId('invoiceDate').max = today;
        byId('receivedDate').max = today;
    }

    function fillSelect(selectId, items, valueKey, labelBuilder, placeholder) {
        const select = byId(selectId);
        if (!select) return;
        select.innerHTML = [
            `<option value="">${escapeHtml(placeholder)}</option>`,
            ...items.map(item =>
                `<option value="${escapeHtml(item[valueKey])}">${escapeHtml(labelBuilder(item))}</option>`)
        ].join('');
    }

    async function loadBootstrap() {
        const data = await getJson('/Admin/Inventory/Receiving/Bootstrap');
        state.bootstrap = data;

        fillSelect(
            'supplierId',
            data.suppliers || [],
            'supplierId',
            item => item.taxCode
                ? `${item.supplierName} · MST ${item.taxCode}`
                : item.supplierName,
            'Chọn nhà cung cấp');

        fillSelect(
            'warehouseId',
            data.warehouses || [],
            'warehouseId',
            item => `${item.warehouseCode} · ${item.warehouseName}`,
            'Chọn kho nhận');

        fillSelect(
            'categoryFilter',
            data.categories || [],
            'categoryId',
            item => item.categoryName,
            'Tất cả danh mục');

        fillSelect(
            'brandFilter',
            data.brands || [],
            'brandId',
            item => item.brandName,
            'Tất cả thương hiệu');

        const primaryWarehouse = (data.warehouses || []).find(item => item.isPrimary)
            || data.warehouses?.[0];
        if (primaryWarehouse) {
            byId('warehouseId').value = String(primaryWarehouse.warehouseId);
            updateWarehouseMeta();
        }

        setStatus('Dữ liệu kho đã sẵn sàng', 'is-ready');
    }

    function updateSupplierMeta() {
        const supplier = state.bootstrap?.suppliers?.find(
            item => Number(item.supplierId) === integerValue('supplierId'));
        byId('supplierMeta').textContent = supplier
            ? [supplier.contactName, supplier.phone, supplier.taxCode && `MST ${supplier.taxCode}`]
                .filter(Boolean)
                .join(' · ') || 'Đã chọn nhà cung cấp.'
            : 'Chọn đúng pháp nhân phát hành chứng từ.';
        invalidatePreview();
    }

    function updateWarehouseMeta() {
        const warehouse = state.bootstrap?.warehouses?.find(
            item => Number(item.warehouseId) === integerValue('warehouseId'));
        byId('warehouseMeta').textContent = warehouse
            ? `${warehouse.warehouseCode} · ${warehouse.address || 'Chưa có địa chỉ'}`
            : 'Tồn hiển thị theo kho đang chọn.';
        state.page = 1;
        invalidatePreview();
        loadProducts();
    }

    function productQueryString() {
        const params = new URLSearchParams({
            page: String(state.page),
            pageSize: String(state.pageSize),
            stockScope: value('stockFilter') || 'all'
        });
        const warehouseId = integerValue('warehouseId');
        if (warehouseId > 0) params.set('warehouseId', String(warehouseId));
        const q = value('productSearch');
        if (q) params.set('q', q);
        const categoryId = integerValue('categoryFilter');
        if (categoryId > 0) params.set('categoryId', String(categoryId));
        const brandId = integerValue('brandFilter');
        if (brandId > 0) params.set('brandId', String(brandId));
        return params.toString();
    }

    async function loadProducts() {
        if (integerValue('warehouseId') <= 0) {
            renderProductEmpty('Chọn kho nhận để tải danh sách sản phẩm.');
            return;
        }

        const sequence = ++state.requestSequence;
        state.loadingProducts = true;
        renderProductSkeleton();

        try {
            const data = await getJson(`/Admin/Inventory/Receiving/Products?${productQueryString()}`);
            if (sequence !== state.requestSequence) return;
            state.products = data.items || [];
            state.page = data.page || 1;
            state.totalPages = data.totalPages || 1;
            state.totalItems = data.totalItems || 0;
            renderProducts();
        } catch (error) {
            if (sequence !== state.requestSequence) return;
            renderProductEmpty(error.message || 'Không tải được danh sách sản phẩm.');
        } finally {
            if (sequence === state.requestSequence) state.loadingProducts = false;
        }
    }

    function renderProductSkeleton() {
        const grid = byId('productGrid');
        grid.innerHTML = Array.from({ length: 8 }, () =>
            '<div class="ir-skeleton" style="height:132px;border-radius:12px"></div>').join('');
    }

    function renderProductEmpty(message) {
        byId('productGrid').innerHTML = `<div class="ir-empty-state">${escapeHtml(message)}</div>`;
        byId('productResultCount').textContent = '0 biến thể';
        byId('pageIndicator').textContent = 'Trang 1 / 1';
        byId('previousPageButton').disabled = true;
        byId('nextPageButton').disabled = true;
    }

    function stockPresentation(stock) {
        const quantity = Number(stock || 0);
        if (quantity <= 0) return { className: 'out', label: 'Hết hàng' };
        if (quantity <= 5) return { className: 'low', label: 'Sắp hết' };
        return { className: 'available', label: 'Đang có tồn' };
    }

    function variantLabel(product) {
        return [product.color, product.storage, product.ram]
            .filter(Boolean)
            .join(' / ') || 'Biến thể mặc định';
    }

    function renderProducts() {
        const grid = byId('productGrid');
        if (state.products.length === 0) {
            renderProductEmpty('Không có biến thể phù hợp với bộ lọc hiện tại.');
            return;
        }

        grid.innerHTML = state.products.map(product => {
            const stock = stockPresentation(product.stock);
            const selected = state.lines.has(Number(product.variantId));
            return `
                <article class="ir-product-card ${selected ? 'is-selected' : ''}"
                         data-variant-id="${product.variantId}" tabindex="0" role="button"
                         aria-label="Thêm ${escapeHtml(product.productName)} vào phiếu">
                    ${selected ? '<span class="ir-selected-mark"><i class="fa-solid fa-check"></i></span>' : ''}
                    <img class="ir-product-image" src="${escapeHtml(product.imageUrl)}"
                         alt="${escapeHtml(product.productName)}"
                         onerror="this.src='/images/products/default-product.png'" />
                    <div>
                        <h3 class="ir-product-name">${escapeHtml(product.productName)}</h3>
                        <div class="ir-product-variant">${escapeHtml(variantLabel(product))}<br>${escapeHtml(product.sku)}</div>
                    </div>
                    <div class="ir-product-footer">
                        <span class="ir-stock-pill ${stock.className}">${stock.label}: ${product.stock}</span>
                        <span class="ir-last-cost">Giá nhập gần nhất: ${formatMoney(product.lastImportPrice)}</span>
                    </div>
                </article>`;
        }).join('');

        grid.querySelectorAll('.ir-product-card').forEach(card => {
            const activate = () => addProduct(Number(card.dataset.variantId));
            card.addEventListener('click', activate);
            card.addEventListener('keydown', event => {
                if (event.key === 'Enter' || event.key === ' ') {
                    event.preventDefault();
                    activate();
                }
            });
        });

        byId('productResultCount').textContent = `${state.totalItems} biến thể`;
        byId('pageIndicator').textContent = `Trang ${state.page} / ${state.totalPages}`;
        byId('previousPageButton').disabled = state.page <= 1;
        byId('nextPageButton').disabled = state.page >= state.totalPages;
    }

    function addProduct(variantId) {
        const product = state.products.find(item => Number(item.variantId) === variantId);
        if (!product) return;

        const existing = state.lines.get(variantId);
        if (existing) {
            existing.quantity += 1;
        } else {
            state.lines.set(variantId, {
                variantId,
                productName: product.productName,
                variantLabel: variantLabel(product),
                sku: product.sku,
                imageUrl: product.imageUrl,
                quantity: 1,
                importPrice: Number(product.lastImportPrice || 0),
                taxRate: 0.1
            });
        }
        invalidatePreview();
        renderLines();
        renderProducts();
    }

    function removeLine(variantId) {
        state.lines.delete(variantId);
        invalidatePreview();
        renderLines();
        renderProducts();
    }

    function updateLine(variantId, field, rawValue) {
        const line = state.lines.get(variantId);
        if (!line) return;
        const parsed = field === 'quantity'
            ? Number.parseInt(rawValue || '0', 10)
            : Number.parseFloat(rawValue || '0');
        line[field] = Number.isFinite(parsed) ? parsed : 0;
        invalidatePreview();
        renderLines(false);
    }

    function renderLines(rebuild = true) {
        const body = byId('receiptLineBody');
        const lines = Array.from(state.lines.values());
        byId('selectedLineCount').textContent = lines.length === 0
            ? 'Chưa chọn mặt hàng'
            : `${lines.length} dòng · ${lines.reduce((sum, line) => sum + Math.max(0, line.quantity || 0), 0)} đơn vị`;

        if (lines.length === 0) {
            body.innerHTML = '<tr class="ir-lines-empty"><td colspan="6">Chọn sản phẩm ở danh sách phía trên để bắt đầu phiếu nhận.</td></tr>';
            updateLocalSummary();
            return;
        }

        if (rebuild) {
            body.innerHTML = lines.map(line => {
                const lineGoods = Number(line.quantity || 0) * Number(line.importPrice || 0);
                return `
                    <tr data-line-id="${line.variantId}">
                        <td>
                            <div class="ir-line-product">
                                <img src="${escapeHtml(line.imageUrl)}" alt="${escapeHtml(line.productName)}"
                                     onerror="this.src='/images/products/default-product.png'" />
                                <div>
                                    <strong>${escapeHtml(line.productName)}</strong>
                                    <small>${escapeHtml(line.variantLabel)} · ${escapeHtml(line.sku)}</small>
                                </div>
                            </div>
                        </td>
                        <td><input class="ir-line-input" type="number" min="1" max="10000"
                                   value="${line.quantity}" data-field="quantity" /></td>
                        <td><input class="ir-line-input price" type="number" min="1" step="1000"
                                   value="${line.importPrice}" data-field="importPrice" /></td>
                        <td>
                            <select class="ir-line-input" data-field="taxRate">
                                <option value="0" ${line.taxRate === 0 ? 'selected' : ''}>0%</option>
                                <option value="0.05" ${line.taxRate === 0.05 ? 'selected' : ''}>5%</option>
                                <option value="0.08" ${line.taxRate === 0.08 ? 'selected' : ''}>8%</option>
                                <option value="0.1" ${line.taxRate === 0.1 ? 'selected' : ''}>10%</option>
                            </select>
                        </td>
                        <td><strong>${formatMoney(lineGoods)}</strong></td>
                        <td><button class="ir-line-remove" type="button" title="Xóa dòng"><i class="fa-solid fa-xmark"></i></button></td>
                    </tr>`;
            }).join('');

            body.querySelectorAll('tr[data-line-id]').forEach(row => {
                const id = Number(row.dataset.lineId);
                row.querySelectorAll('[data-field]').forEach(control => {
                    control.addEventListener('change', event =>
                        updateLine(id, event.currentTarget.dataset.field, event.currentTarget.value));
                });
                row.querySelector('.ir-line-remove')?.addEventListener('click', () => removeLine(id));
            });
        } else {
            body.querySelectorAll('tr[data-line-id]').forEach(row => {
                const line = state.lines.get(Number(row.dataset.lineId));
                if (!line) return;
                const amountCell = row.children[4];
                if (amountCell) {
                    amountCell.innerHTML = `<strong>${formatMoney(line.quantity * line.importPrice)}</strong>`;
                }
            });
        }
        updateLocalSummary();
    }

    function updateLocalSummary() {
        const lines = Array.from(state.lines.values());
        const goods = lines.reduce((sum, line) =>
            sum + Math.max(0, line.quantity) * Math.max(0, line.importPrice), 0);
        const vat = lines.reduce((sum, line) =>
            sum + Math.max(0, line.quantity) * Math.max(0, line.importPrice) * Math.max(0, line.taxRate), 0);
        const fees = Math.max(0, numberValue('shippingFee')) + Math.max(0, numberValue('otherFee'));
        const deductible = byId('inputVatDeductible').checked;
        byId('goodsSubtotal').textContent = formatMoney(goods);
        byId('inputVatAmount').textContent = formatMoney(vat);
        byId('allocatedFeeTotal').textContent = formatMoney(fees);
        byId('supplierPayable').textContent = formatMoney(goods + vat + fees);
        byId('capitalizedCost').textContent = formatMoney(goods + fees + (deductible ? 0 : vat));
    }

    function buildPayload() {
        return {
            supplierId: integerValue('supplierId'),
            warehouseId: integerValue('warehouseId'),
            invoiceNumber: value('invoiceNumber'),
            invoiceDate: value('invoiceDate') || null,
            receivedDate: value('receivedDate') || null,
            shippingFee: numberValue('shippingFee'),
            otherFee: numberValue('otherFee'),
            inputVatDeductible: byId('inputVatDeductible').checked,
            note: value('receiptNote'),
            items: Array.from(state.lines.values()).map(line => ({
                variantId: Number(line.variantId),
                quantity: Number(line.quantity),
                importPrice: Number(line.importPrice),
                taxRate: Number(line.taxRate)
            }))
        };
    }

    function payloadFingerprint(payload) {
        return JSON.stringify(payload);
    }

    function invalidatePreview() {
        state.previewValid = false;
        state.previewFingerprint = '';
        byId('submitButton').disabled = true;
        const preview = byId('previewState');
        preview.classList.remove('is-valid', 'is-error');
        preview.textContent = state.lines.size === 0
            ? 'Thêm sản phẩm để xem trước landed cost.'
            : 'Dữ liệu đã thay đổi. Hãy kiểm tra lại trước khi ghi nhận.';
        byId('previewWarnings').innerHTML = '';
        updateLocalSummary();
    }

    function validateClient(payload) {
        if (payload.supplierId <= 0) return 'Vui lòng chọn nhà cung cấp.';
        if (payload.warehouseId <= 0) return 'Vui lòng chọn kho nhận.';
        if (!payload.invoiceNumber || payload.invoiceNumber.length < 3) return 'Vui lòng nhập số hóa đơn/chứng từ hợp lệ.';
        if (!payload.invoiceDate) return 'Vui lòng chọn ngày chứng từ.';
        if (!payload.receivedDate) return 'Vui lòng chọn ngày nhận hàng.';
        if (payload.items.length === 0) return 'Phiếu nhập chưa có mặt hàng.';
        if (payload.items.some(item => item.quantity <= 0 || item.importPrice <= 0)) {
            return 'Số lượng và giá nhập của mọi dòng phải lớn hơn 0.';
        }
        return '';
    }

    async function previewReceipt() {
        const payload = buildPayload();
        const clientError = validateClient(payload);
        const preview = byId('previewState');
        if (clientError) {
            preview.className = 'ir-preview-state is-error';
            preview.textContent = clientError;
            return;
        }

        const button = byId('previewButton');
        button.disabled = true;
        button.innerHTML = '<i class="fa-solid fa-circle-notch fa-spin"></i> Đang kiểm tra';
        try {
            const data = await postJson('/Admin/Inventory/Receiving/Preview', payload);
            byId('goodsSubtotal').textContent = formatMoney(data.goodsSubtotal);
            byId('inputVatAmount').textContent = formatMoney(data.inputVatAmount);
            byId('allocatedFeeTotal').textContent = formatMoney(Number(data.shippingFee) + Number(data.otherFee));
            byId('supplierPayable').textContent = formatMoney(data.supplierPayable);
            byId('capitalizedCost').textContent = formatMoney(data.inventoryCapitalizedCost);

            preview.className = 'ir-preview-state is-valid';
            preview.textContent = `Hợp lệ: ${data.lines.length} dòng đã được phân bổ phí nhập và tính landed cost.`;
            byId('previewWarnings').innerHTML = (data.warnings || [])
                .map(message => `<div class="ir-warning-item">${escapeHtml(message)}</div>`)
                .join('');

            state.previewValid = true;
            state.previewFingerprint = payloadFingerprint(payload);
            byId('submitButton').disabled = false;
        } catch (error) {
            preview.className = 'ir-preview-state is-error';
            preview.textContent = error.message || 'Không thể kiểm tra phiếu nhập.';
            state.previewValid = false;
            state.previewFingerprint = '';
            byId('submitButton').disabled = true;
        } finally {
            button.disabled = false;
            button.innerHTML = '<i class="fa-solid fa-calculator"></i> Kiểm tra & xem trước';
        }
    }

    async function submitReceipt() {
        const payload = buildPayload();
        if (!state.previewValid || state.previewFingerprint !== payloadFingerprint(payload)) {
            invalidatePreview();
            byId('previewState').className = 'ir-preview-state is-error';
            byId('previewState').textContent = 'Dữ liệu đã thay đổi sau lần xem trước. Hãy kiểm tra lại.';
            return;
        }

        const button = byId('submitButton');
        button.disabled = true;
        button.innerHTML = '<i class="fa-solid fa-circle-notch fa-spin"></i> Đang ghi nhận transaction';
        try {
            const data = await postJson('/Admin/Inventory/Receiving/Submit', payload);
            byId('resultModalMessage').textContent = data.message || 'Đã cập nhật tồn kho.';
            byId('resultModalDetail').innerHTML = `
                <strong>Mã phiếu:</strong> ${escapeHtml(data.poCode)}<br>
                <strong>Chứng từ NCC:</strong> ${escapeHtml(data.invoiceNumber)}<br>
                <strong>Số lượng nhận:</strong> ${escapeHtml(data.totalQuantity)} đơn vị<br>
                <strong>Phải trả NCC:</strong> ${formatMoney(data.supplierPayable)}<br>
                <strong>Vốn hóa tồn kho:</strong> ${formatMoney(data.inventoryCapitalizedCost)}`;
            byId('resultModal').hidden = false;
            await loadRecentReceipts();
        } catch (error) {
            byId('previewState').className = 'ir-preview-state is-error';
            byId('previewState').textContent = error.message || 'Không thể ghi nhận phiếu nhập.';
            button.disabled = false;
        } finally {
            button.innerHTML = '<i class="fa-solid fa-boxes-stacked"></i> Xác nhận nhận hàng vào kho';
        }
    }

    async function loadRecentReceipts() {
        const container = byId('recentReceiptList');
        try {
            const data = await getJson('/Admin/Inventory/Receiving/Recent?take=8');
            const receipts = data.receipts || [];
            if (receipts.length === 0) {
                container.innerHTML = '<div class="ir-empty-state" style="min-height:150px">Chưa có phiếu nhập.</div>';
                return;
            }
            container.innerHTML = receipts.map(receipt => `
                <div class="ir-recent-item">
                    <span class="ir-recent-code">${escapeHtml(receipt.poCode)}</span>
                    <span class="ir-recent-amount">${formatMoney(receipt.totalAmount)}</span>
                    <span class="ir-recent-meta">
                        ${escapeHtml(receipt.supplierName)} · ${escapeHtml(receipt.warehouseName)}<br>
                        ${escapeHtml(receipt.invoiceNumber || 'Không có chứng từ')} · ${formatDate(receipt.orderDate)}
                    </span>
                </div>`).join('');
        } catch (error) {
            container.innerHTML = `<div class="ir-empty-state" style="min-height:150px">${escapeHtml(error.message)}</div>`;
        }
    }

    function formatDate(value) {
        if (!value) return 'Chưa có ngày';
        const date = new Date(value);
        return Number.isNaN(date.getTime())
            ? 'Ngày không hợp lệ'
            : new Intl.DateTimeFormat('vi-VN').format(date);
    }

    function resetReceipt() {
        state.lines.clear();
        byId('invoiceNumber').value = '';
        byId('receiptNote').value = '';
        byId('shippingFee').value = '0';
        byId('otherFee').value = '0';
        byId('inputVatDeductible').checked = true;
        setTodayDefaults();
        invalidatePreview();
        renderLines();
        renderProducts();
        byId('resultModal').hidden = true;
        byId('invoiceNumber').focus();
    }

    function bindEvents() {
        let searchTimer = null;
        byId('productSearch').addEventListener('input', () => {
            clearTimeout(searchTimer);
            searchTimer = setTimeout(() => {
                state.page = 1;
                loadProducts();
            }, 280);
        });

        ['categoryFilter', 'brandFilter', 'stockFilter'].forEach(id => {
            byId(id).addEventListener('change', () => {
                state.page = 1;
                loadProducts();
            });
        });

        byId('resetFilterButton').addEventListener('click', () => {
            byId('productSearch').value = '';
            byId('categoryFilter').value = '';
            byId('brandFilter').value = '';
            byId('stockFilter').value = 'all';
            state.page = 1;
            loadProducts();
        });

        byId('previousPageButton').addEventListener('click', () => {
            if (state.page <= 1) return;
            state.page -= 1;
            loadProducts();
        });
        byId('nextPageButton').addEventListener('click', () => {
            if (state.page >= state.totalPages) return;
            state.page += 1;
            loadProducts();
        });

        byId('supplierId').addEventListener('change', updateSupplierMeta);
        byId('warehouseId').addEventListener('change', updateWarehouseMeta);
        byId('clearLinesButton').addEventListener('click', () => {
            state.lines.clear();
            invalidatePreview();
            renderLines();
            renderProducts();
        });

        ['invoiceNumber', 'invoiceDate', 'receivedDate', 'receiptNote'].forEach(id =>
            byId(id).addEventListener('input', invalidatePreview));
        ['shippingFee', 'otherFee'].forEach(id =>
            byId(id).addEventListener('input', invalidatePreview));
        byId('inputVatDeductible').addEventListener('change', invalidatePreview);

        byId('previewButton').addEventListener('click', previewReceipt);
        byId('submitButton').addEventListener('click', submitReceipt);
        byId('reloadRecentButton').addEventListener('click', loadRecentReceipts);
        byId('createAnotherButton').addEventListener('click', resetReceipt);
    }

    async function initialize() {
        setTodayDefaults();
        renderLines();
        bindEvents();
        try {
            await Promise.all([loadBootstrap(), loadRecentReceipts()]);
            await loadProducts();
        } catch (error) {
            setStatus('Không thể khởi tạo workspace', 'is-error');
            showPageWarning(
                `${error.message || 'Lỗi tải dữ liệu.'} Có thể quay lại màn hình kiểm soát kho và kiểm tra migration tồn kho hiện hành.`);
        }
    }

    document.addEventListener('DOMContentLoaded', initialize);
})();
