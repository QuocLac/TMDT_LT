(() => {
    'use strict';

    const api = {
        operations: '/Admin/Inventory/Operations',
        outbound: '/Admin/Inventory/Distribution',
        transfer: '/Admin/Inventory/Transfers',
        counting: '/Admin/Inventory/Counts',
        inventory: '/Admin/Inventory'
    };

    const state = {
        bootstrap: null,
        activeTab: 'outbound',
        outbound: { page: 1, totalPages: 1, products: [], lines: new Map(), checked: false },
        transfer: { page: 1, totalPages: 1, products: [], lines: new Map(), checked: false },
        count: { page: 1, totalPages: 1, products: [], selected: new Map(), sessionId: null, session: null, lines: [] }
    };

    const $ = id => document.getElementById(id);
    const money = value => `${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 }).format(Number(value || 0))} đ`;
    const number = value => new Intl.NumberFormat('vi-VN').format(Number(value || 0));
    const dateTime = value => value ? new Date(value).toLocaleString('vi-VN') : '—';
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
            body: JSON.stringify(payload ?? {})
        }));
    }

    function toast(message, type = 'info') {
        const stack = $('inventoryToastStack');
        if (!stack) return;
        const item = document.createElement('div');
        item.className = `inventory-toast ${type}`;
        item.textContent = message;
        stack.appendChild(item);
        setTimeout(() => item.remove(), 4300);
    }

    function fillSelect(id, items, valueKey, labelBuilder, placeholder = '') {
        const select = $(id);
        if (!select) return;
        select.innerHTML = [
            placeholder ? `<option value="">${escapeHtml(placeholder)}</option>` : '',
            ...items.map(item => `<option value="${escapeHtml(item[valueKey])}">${escapeHtml(labelBuilder(item))}</option>`)
        ].join('');
    }

    function productCard(item, action, selected = false) {
        return `
            <article class="inventory-product-card ${selected ? 'is-selected' : ''}">
                <img src="${escapeHtml(item.imageUrl)}" alt="${escapeHtml(item.productName)}" onerror="this.src='/images/products/default-product.png'" />
                <div class="inventory-product-card-body">
                    <strong>${escapeHtml(item.productName)}</strong>
                    <small>${escapeHtml(item.sku)} · ${escapeHtml(item.variantLabel)}</small>
                    <small>${escapeHtml(item.brandName)} · ${escapeHtml(item.categoryName)}</small>
                </div>
                <div class="inventory-product-card-foot">
                    <span>Có thể bán <strong>${number(item.available)}</strong></span>
                    <button type="button" ${item.available <= 0 ? 'disabled' : ''} ${action}>${selected ? 'Đã chọn' : 'Thêm'}</button>
                </div>
            </article>`;
    }


    function countProductCard(item, selected = false) {
        return `
            <article class="inventory-product-card ${selected ? 'is-selected' : ''}">
                <img src="${escapeHtml(item.imageUrl)}" alt="${escapeHtml(item.productName)}" onerror="this.src='/images/products/default-product.png'" />
                <div class="inventory-product-card-body">
                    <strong>${escapeHtml(item.productName)}</strong>
                    <small>${escapeHtml(item.sku)} · ${escapeHtml(item.variantLabel)}</small>
                    <small>${escapeHtml(item.brandName)} · ${escapeHtml(item.categoryName)}</small>
                </div>
                <div class="inventory-product-card-foot">
                    <span>Tồn hệ thống <strong>${number(item.onHand)}</strong></span>
                    <button type="button" data-toggle-count="${item.variantId}">${selected ? 'Đã chọn' : 'Chọn'}</button>
                </div>
            </article>`;
    }

    async function loadBootstrap() {
        const data = await getJson(`${api.operations}/Bootstrap`);
        state.bootstrap = data;
        const warehouses = data.warehouses || [];
        const stores = data.stores || [];
        const categories = data.categories || [];
        const brands = data.brands || [];

        ['outboundWarehouse', 'transferSourceWarehouse', 'transferTargetWarehouse', 'countWarehouse'].forEach(id =>
            fillSelect(id, warehouses, 'warehouseId', item => `${item.warehouseCode} · ${item.warehouseName}`));
        fillSelect('outboundStore', stores, 'storeId', item => `${item.storeCode} · ${item.storeName}`, 'Chọn nơi nhận');
        fillSelect('outboundCategory', categories, 'categoryId', item => item.categoryName, 'Tất cả danh mục');
        fillSelect('outboundBrand', brands, 'brandId', item => item.brandName, 'Tất cả thương hiệu');

        const source = $('transferSourceWarehouse');
        const target = $('transferTargetWarehouse');
        if (source && target && source.value === target.value) {
            const other = [...target.options].find(option => option.value !== source.value);
            if (other) target.value = other.value;
        }
    }

    function switchTab(tab) {
        if (!['outbound', 'transfer', 'counting'].includes(tab)) tab = 'outbound';
        state.activeTab = tab;
        document.querySelectorAll('[data-operation-tab]').forEach(button =>
            button.classList.toggle('is-active', button.dataset.operationTab === tab));
        document.querySelectorAll('[data-operation-panel]').forEach(panel =>
            panel.hidden = panel.dataset.operationPanel !== tab);

        const url = new URL(window.location.href);
        url.searchParams.set('tab', tab);
        history.replaceState(null, '', url);

        if (tab === 'outbound') Promise.all([loadOutboundProducts(), loadOutboundRecent()]);
        if (tab === 'transfer') Promise.all([loadTransferProducts(), loadTransferRecent()]);
        if (tab === 'counting') Promise.all([loadCountProducts(), loadCountSessions()]);
    }

    // -------------------- Xuất kho --------------------
    function outboundQuery() {
        return new URLSearchParams({
            warehouseId: $('outboundWarehouse').value,
            q: $('outboundSearch').value.trim(),
            categoryId: $('outboundCategory').value,
            brandId: $('outboundBrand').value,
            stockScope: 'available',
            page: String(state.outbound.page),
            pageSize: '12'
        });
    }

    async function loadOutboundProducts(reset = false) {
        if (reset) state.outbound.page = 1;
        const grid = $('outboundProductGrid');
        grid.innerHTML = '<div class="inventory-empty">Đang tải mặt hàng...</div>';
        try {
            const data = await getJson(`${api.operations}/Products?${outboundQuery()}`);
            state.outbound.products = data.items || [];
            state.outbound.page = data.page || 1;
            state.outbound.totalPages = data.totalPages || 1;
            $('outboundPageMeta').textContent = `Trang ${state.outbound.page} / ${state.outbound.totalPages} · ${number(data.totalItems)} mặt hàng`;
            $('outboundPrevPage').disabled = state.outbound.page <= 1;
            $('outboundNextPage').disabled = state.outbound.page >= state.outbound.totalPages;
            grid.innerHTML = state.outbound.products.length
                ? state.outbound.products.map(item => productCard(item, `data-add-outbound="${item.variantId}"`, state.outbound.lines.has(item.variantId))).join('')
                : '<div class="inventory-empty">Không có mặt hàng có thể xuất.</div>';
        } catch (error) {
            grid.innerHTML = `<div class="inventory-empty">${escapeHtml(error.message)}</div>`;
            toast(error.message, 'error');
        }
    }

    function addOutbound(variantId) {
        const item = state.outbound.products.find(product => product.variantId === variantId);
        if (!item || item.available <= 0) return;
        const line = state.outbound.lines.get(variantId);
        if (line) {
            line.quantity = Math.min(line.available, line.quantity + 1);
        } else {
            state.outbound.lines.set(variantId, {
                ...item,
                quantity: 1,
                exportPrice: Number(item.price || 0),
                taxRate: 0.1
            });
        }
        invalidateOutbound();
        renderOutboundLines();
        loadOutboundProducts();
    }

    function renderOutboundLines() {
        const body = $('outboundLineBody');
        const lines = [...state.outbound.lines.values()];
        if (!lines.length) {
            body.innerHTML = '<tr><td colspan="7" class="inventory-empty">Chưa có mặt hàng trong phiếu.</td></tr>';
            return;
        }
        body.innerHTML = lines.map(item => `
            <tr>
                <td><div class="inventory-product-cell"><img src="${escapeHtml(item.imageUrl)}" onerror="this.src='/images/products/default-product.png'" alt=""><div><strong>${escapeHtml(item.productName)}</strong><small>${escapeHtml(item.sku)} · ${escapeHtml(item.variantLabel)}</small></div></div></td>
                <td>${number(item.available)}</td>
                <td><input class="inventory-table-input" type="number" min="1" max="${item.available}" value="${item.quantity}" data-outbound-field="quantity" data-id="${item.variantId}"></td>
                <td><input class="inventory-table-input" type="number" min="1" step="1000" value="${item.exportPrice}" data-outbound-field="exportPrice" data-id="${item.variantId}"></td>
                <td><select class="inventory-table-input" data-outbound-field="taxRate" data-id="${item.variantId}"><option value="0" ${item.taxRate === 0 ? 'selected' : ''}>0%</option><option value="0.08" ${item.taxRate === 0.08 ? 'selected' : ''}>8%</option><option value="0.1" ${item.taxRate === 0.1 ? 'selected' : ''}>10%</option></select></td>
                <td class="is-number">${money(item.quantity * item.exportPrice)}</td>
                <td><button class="inventory-remove-button" type="button" data-remove-outbound="${item.variantId}">Xóa</button></td>
            </tr>`).join('');
    }

    function outboundPayload() {
        return {
            storeId: Number($('outboundStore').value),
            fromWarehouseId: Number($('outboundWarehouse').value),
            invoiceNumber: $('outboundInvoice').value.trim(),
            discountPercent: Number($('outboundDiscount').value || 0),
            shippingCost: Number($('outboundShipping').value || 0),
            notes: $('outboundNotes').value.trim(),
            items: [...state.outbound.lines.values()].map(item => ({
                variantId: item.variantId,
                quantity: Number(item.quantity),
                exportPrice: Number(item.exportPrice),
                taxRate: Number(item.taxRate)
            }))
        };
    }

    function validateOutbound(payload) {
        if (!payload.fromWarehouseId || !payload.storeId) return 'Hãy chọn kho xuất và nơi nhận.';
        if (!payload.items.length) return 'Hãy thêm ít nhất một mặt hàng.';
        if (payload.items.some(item => item.quantity <= 0 || item.exportPrice <= 0)) return 'Số lượng và giá bán phải lớn hơn 0.';
        if (payload.items.some(item => item.quantity > (state.outbound.lines.get(item.variantId)?.available || 0))) return 'Có mặt hàng vượt số lượng có thể bán.';
        if (payload.discountPercent < 0 || payload.discountPercent > 100 || payload.shippingCost < 0) return 'Chiết khấu hoặc phí giao hàng không hợp lệ.';
        return '';
    }

    function invalidateOutbound() {
        state.outbound.checked = false;
        $('submitOutboundButton').disabled = true;
        $('outboundStatus').className = 'inventory-alert';
        $('outboundStatus').textContent = state.outbound.lines.size ? 'Thông tin đã thay đổi. Hãy kiểm tra lại phiếu.' : 'Thêm mặt hàng để bắt đầu.';
        ['outboundNetRevenue', 'outboundTax', 'outboundInvoiceTotal', 'outboundCost', 'outboundShippingSummary', 'outboundProfit'].forEach(id => $(id).textContent = '—');
    }

    async function checkOutbound() {
        const payload = outboundPayload();
        const validation = validateOutbound(payload);
        if (validation) return toast(validation, 'error');
        const status = $('outboundStatus');
        status.textContent = 'Đang kiểm tra số lượng và tính tổng phiếu...';
        try {
            const result = await postJson(`${api.outbound}/Preview`, payload);
            state.outbound.checked = true;
            $('outboundNetRevenue').textContent = money(result.netRevenue);
            $('outboundTax').textContent = money(result.taxAmount);
            $('outboundInvoiceTotal').textContent = money(result.invoiceTotal);
            $('outboundCost').textContent = money(result.cogs);
            $('outboundShippingSummary').textContent = money(result.shippingCost);
            $('outboundProfit').textContent = money(result.realizedProfit);
            status.className = 'inventory-alert inventory-alert-success';
            status.textContent = 'Phiếu hợp lệ. Hệ thống sẽ tự xuất từ lô nhập trước.';
            $('submitOutboundButton').disabled = false;
        } catch (error) {
            state.outbound.checked = false;
            status.className = 'inventory-alert inventory-alert-error';
            status.textContent = error.message;
            $('submitOutboundButton').disabled = true;
        }
    }

    async function submitOutbound() {
        if (!state.outbound.checked) return toast('Hãy kiểm tra phiếu trước khi xác nhận.', 'error');
        if (!confirm('Xác nhận xuất các mặt hàng đã chọn khỏi kho?')) return;
        const button = $('submitOutboundButton');
        button.disabled = true;
        try {
            const result = await postJson(`${api.outbound}/Submit`, outboundPayload());
            toast(`${result.soCode}: đã xuất kho thành công.`, 'success');
            state.outbound.lines.clear();
            renderOutboundLines();
            ['outboundInvoice', 'outboundNotes'].forEach(id => $(id).value = '');
            $('outboundDiscount').value = '0';
            $('outboundShipping').value = '0';
            invalidateOutbound();
            await Promise.all([loadOutboundProducts(true), loadOutboundRecent()]);
        } catch (error) {
            toast(error.message, 'error');
            button.disabled = false;
        }
    }

    async function loadOutboundRecent() {
        const body = $('outboundRecentBody');
        body.innerHTML = '<tr><td colspan="7" class="inventory-empty">Đang tải...</td></tr>';
        try {
            const data = await getJson(`${api.outbound}/Recent?take=10`);
            const rows = data.distributions || [];
            body.innerHTML = rows.length ? rows.map(item => `
                <tr><td><strong>${escapeHtml(item.soCode)}</strong><small>${escapeHtml(item.status)}</small></td><td>${dateTime(item.orderDate)}</td><td>${escapeHtml(item.warehouseName)} → ${escapeHtml(item.storeName)}</td><td>${number(item.totalQuantity)}</td><td class="is-number">${money(item.totalAmount)}</td><td class="is-number">${money(item.cogs)}</td><td class="is-number">${money(item.profit)}</td></tr>`).join('') : '<tr><td colspan="7" class="inventory-empty">Chưa có phiếu xuất.</td></tr>';
        } catch (error) {
            body.innerHTML = `<tr><td colspan="7" class="inventory-empty">${escapeHtml(error.message)}</td></tr>`;
        }
    }

    // -------------------- Chuyển kho --------------------
    function keepWarehousesDifferent(changed) {
        const source = $('transferSourceWarehouse');
        const target = $('transferTargetWarehouse');
        if (!source || !target || source.value !== target.value) return;
        const select = changed === 'source' ? target : source;
        const otherValue = changed === 'source' ? source.value : target.value;
        const other = [...select.options].find(option => option.value !== otherValue);
        if (other) select.value = other.value;
    }

    function transferQuery() {
        return new URLSearchParams({
            warehouseId: $('transferSourceWarehouse').value,
            q: $('transferSearch').value.trim(),
            stockScope: 'available',
            page: String(state.transfer.page),
            pageSize: '12'
        });
    }

    async function loadTransferProducts(reset = false) {
        if (reset) state.transfer.page = 1;
        const grid = $('transferProductGrid');
        grid.innerHTML = '<div class="inventory-empty">Đang tải mặt hàng...</div>';
        try {
            const data = await getJson(`${api.operations}/Products?${transferQuery()}`);
            state.transfer.products = data.items || [];
            state.transfer.page = data.page || 1;
            state.transfer.totalPages = data.totalPages || 1;
            $('transferPageMeta').textContent = `Trang ${state.transfer.page} / ${state.transfer.totalPages} · ${number(data.totalItems)} mặt hàng`;
            $('transferPrevPage').disabled = state.transfer.page <= 1;
            $('transferNextPage').disabled = state.transfer.page >= state.transfer.totalPages;
            grid.innerHTML = state.transfer.products.length
                ? state.transfer.products.map(item => productCard(item, `data-add-transfer="${item.variantId}"`, state.transfer.lines.has(item.variantId))).join('')
                : '<div class="inventory-empty">Không có mặt hàng có thể chuyển.</div>';
        } catch (error) {
            grid.innerHTML = `<div class="inventory-empty">${escapeHtml(error.message)}</div>`;
            toast(error.message, 'error');
        }
    }

    function addTransfer(variantId) {
        const item = state.transfer.products.find(product => product.variantId === variantId);
        if (!item || item.available <= 0) return;
        const line = state.transfer.lines.get(variantId);
        if (line) line.quantity = Math.min(line.available, line.quantity + 1);
        else state.transfer.lines.set(variantId, { ...item, quantity: 1 });
        invalidateTransfer();
        renderTransferLines();
        loadTransferProducts();
    }

    function renderTransferLines() {
        const container = $('transferLineList');
        const lines = [...state.transfer.lines.values()];
        $('transferLineCount').textContent = lines.length ? `${lines.length} mặt hàng` : 'Chưa chọn mặt hàng';
        $('transferTotalQuantity').textContent = number(lines.reduce((sum, item) => sum + Number(item.quantity || 0), 0));
        $('transferEstimatedValue').textContent = money(lines.reduce((sum, item) => sum + Number(item.quantity || 0) * Number(item.averageCost || 0), 0));
        container.innerHTML = lines.length ? lines.map(item => `
            <div class="inventory-cart-item">
                <div><strong>${escapeHtml(item.productName)}</strong><small>${escapeHtml(item.sku)} · ${escapeHtml(item.variantLabel)} · Còn ${number(item.available)}</small></div>
                <input type="number" min="1" max="${item.available}" value="${item.quantity}" data-transfer-quantity="${item.variantId}" />
                <button type="button" data-remove-transfer="${item.variantId}">×</button>
            </div>`).join('') : '<div class="inventory-empty">Chưa có mặt hàng.</div>';
    }

    function transferPayload() {
        return {
            sourceWarehouseId: Number($('transferSourceWarehouse').value),
            targetWarehouseId: Number($('transferTargetWarehouse').value),
            reason: $('transferReason').value.trim(),
            items: [...state.transfer.lines.values()].map(item => ({ variantId: item.variantId, quantity: Number(item.quantity) }))
        };
    }

    function invalidateTransfer() {
        state.transfer.checked = false;
        $('submitTransferButton').disabled = true;
        $('transferStatus').className = 'inventory-alert';
        $('transferStatus').textContent = state.transfer.lines.size ? 'Thông tin đã thay đổi. Hãy kiểm tra lại phiếu.' : 'Chọn mặt hàng cần chuyển.';
        renderTransferLines();
    }

    async function checkTransfer() {
        const payload = transferPayload();
        if (!payload.sourceWarehouseId || !payload.targetWarehouseId || payload.sourceWarehouseId === payload.targetWarehouseId) return toast('Kho chuyển và kho nhận phải khác nhau.', 'error');
        if (payload.reason.length < 5) return toast('Hãy nhập lý do chuyển tối thiểu 5 ký tự.', 'error');
        if (!payload.items.length) return toast('Hãy thêm ít nhất một mặt hàng.', 'error');
        try {
            const result = await postJson(`${api.transfer}/Preview`, payload);
            state.transfer.checked = true;
            $('transferTotalQuantity').textContent = number(result.totalQuantity);
            $('transferEstimatedValue').textContent = money(result.estimatedValue);
            $('transferStatus').className = 'inventory-alert inventory-alert-success';
            $('transferStatus').textContent = 'Số lượng hợp lệ. Có thể xác nhận chuyển kho.';
            $('submitTransferButton').disabled = false;
        } catch (error) {
            state.transfer.checked = false;
            $('transferStatus').className = 'inventory-alert inventory-alert-error';
            $('transferStatus').textContent = error.message;
        }
    }

    async function submitTransfer() {
        if (!state.transfer.checked) return toast('Hãy kiểm tra phiếu trước khi xác nhận.', 'error');
        if (!confirm('Xác nhận chuyển các mặt hàng sang kho nhận?')) return;
        const button = $('submitTransferButton');
        button.disabled = true;
        try {
            const result = await postJson(`${api.transfer}/Submit`, transferPayload());
            toast(`${result.transferCode}: đã chuyển kho thành công.`, 'success');
            state.transfer.lines.clear();
            $('transferReason').value = '';
            invalidateTransfer();
            await Promise.all([loadTransferProducts(true), loadTransferRecent()]);
        } catch (error) {
            toast(error.message, 'error');
            button.disabled = false;
        }
    }

    async function loadTransferRecent() {
        const body = $('transferRecentBody');
        body.innerHTML = '<tr><td colspan="6" class="inventory-empty">Đang tải...</td></tr>';
        try {
            const data = await getJson(`${api.transfer}/Recent?take=10`);
            const rows = data.transfers || [];
            body.innerHTML = rows.length ? rows.map(item => `
                <tr><td><strong>${escapeHtml(item.transferCode || `TRF-${item.referenceId}`)}</strong></td><td>${dateTime(item.transactionDate)}</td><td>${escapeHtml(item.sourceWarehouseName || '—')} → ${escapeHtml(item.targetWarehouseName || '—')}</td><td>${escapeHtml(item.products || '—')}</td><td>${number(item.totalQuantity)}</td><td class="is-number">${money(item.totalValue)}</td></tr>`).join('') : '<tr><td colspan="6" class="inventory-empty">Chưa có lần chuyển kho.</td></tr>';
        } catch (error) {
            body.innerHTML = `<tr><td colspan="6" class="inventory-empty">${escapeHtml(error.message)}</td></tr>`;
        }
    }

    // -------------------- Kiểm kê --------------------
    function countQuery() {
        return new URLSearchParams({
            warehouseId: $('countWarehouse').value,
            q: $('countSearch').value.trim(),
            stockScope: 'all',
            page: String(state.count.page),
            pageSize: '12'
        });
    }

    async function loadCountProducts(reset = false) {
        if (reset) state.count.page = 1;
        const grid = $('countProductGrid');
        grid.innerHTML = '<div class="inventory-empty">Đang tải mặt hàng...</div>';
        try {
            const data = await getJson(`${api.operations}/Products?${countQuery()}`);
            state.count.products = data.items || [];
            state.count.page = data.page || 1;
            state.count.totalPages = data.totalPages || 1;
            $('countPageMeta').textContent = `Trang ${state.count.page} / ${state.count.totalPages} · ${number(data.totalItems)} mặt hàng`;
            $('countPrevPage').disabled = state.count.page <= 1;
            $('countNextPage').disabled = state.count.page >= state.count.totalPages;
            grid.innerHTML = state.count.products.length ? state.count.products.map(item => {
                const selected = state.count.selected.has(item.variantId);
                return countProductCard(item, selected);
            }).join('') : '<div class="inventory-empty">Không tìm thấy mặt hàng.</div>';
        } catch (error) {
            grid.innerHTML = `<div class="inventory-empty">${escapeHtml(error.message)}</div>`;
        }
    }

    function updateCountSelection() {
        $('countSelectionTotal').textContent = `${number(state.count.selected.size)} mặt hàng đã chọn`;
        loadCountProducts();
    }

    async function createCount() {
        const variantIds = [...state.count.selected.keys()];
        if (!variantIds.length) return toast('Hãy chọn ít nhất một mặt hàng.', 'error');
        try {
            const result = await postJson(api.counting, {
                warehouseId: Number($('countWarehouse').value),
                scopeType: 'Cycle',
                notes: $('countNotes').value.trim(),
                variantIds
            });
            toast(result.message || 'Đã tạo đợt kiểm kê.', 'success');
            state.count.selected.clear();
            $('countNotes').value = '';
            updateCountSelection();
            await loadCountSessions();
            await openCountSession(result.countSessionId);
        } catch (error) {
            toast(error.message, 'error');
        }
    }

    function countStatusLabel(status) {
        return ({ Counting: 'Đang kiểm đếm', PendingApproval: 'Chờ xác nhận', Posted: 'Đã hoàn tất', Cancelled: 'Đã hủy' })[status] || status;
    }

    async function loadCountSessions() {
        const body = $('countSessionBody');
        body.innerHTML = '<tr><td colspan="7" class="inventory-empty">Đang tải...</td></tr>';
        try {
            const warehouseId = Number($('countWarehouse').value || 0);
            const data = await getJson(`${api.counting}?warehouseId=${warehouseId}&take=30`);
            const rows = data.sessions || [];
            body.innerHTML = rows.length ? rows.map(item => `
                <tr><td><strong>${escapeHtml(item.countCode)}</strong></td><td>${escapeHtml(item.warehouseName)}</td><td>${number(item.countedLineCount)} / ${number(item.lineCount)}</td><td>${number(item.discrepancyLineCount)}</td><td><span class="inventory-status-badge">${escapeHtml(countStatusLabel(item.status))}</span></td><td>${dateTime(item.createdAt)}</td><td><button class="inventory-link-button" type="button" data-open-count="${item.countSessionId}">Mở</button></td></tr>`).join('') : '<tr><td colspan="7" class="inventory-empty">Chưa có đợt kiểm kê.</td></tr>';
        } catch (error) {
            body.innerHTML = `<tr><td colspan="7" class="inventory-empty">${escapeHtml(error.message)}</td></tr>`;
        }
    }

    async function openCountSession(sessionId) {
        try {
            const data = await getJson(`${api.counting}/${sessionId}`);
            state.count.sessionId = sessionId;
            state.count.session = data.session;
            state.count.lines = data.lines || [];
            $('countDetailPanel').hidden = false;
            $('countDetailTitle').textContent = data.session.countCode;
            $('countDetailMeta').textContent = `${data.session.warehouseName} · ${countStatusLabel(data.session.status)} · ${state.count.lines.length} mặt hàng`;
            $('countDetailStatus').className = 'inventory-alert';
            $('countDetailStatus').textContent = data.session.status === 'Counting'
                ? 'Nhập số lượng thực tế rồi lưu từng dòng.'
                : data.session.status === 'PendingApproval'
                    ? 'Đã nhập đủ số lượng. Kiểm tra lại trước khi hoàn tất.'
                    : 'Đợt kiểm kê này đã kết thúc.';
            $('submitCountButton').hidden = data.session.status !== 'Counting';
            $('postCountButton').hidden = data.session.status !== 'PendingApproval';
            $('cancelCountButton').hidden = !['Counting', 'PendingApproval'].includes(data.session.status);
            renderCountLines();
            $('countDetailPanel').scrollIntoView({ behavior: 'smooth', block: 'start' });
        } catch (error) {
            toast(error.message, 'error');
        }
    }

    function renderCountLines() {
        const body = $('countLineBody');
        const editable = state.count.session?.status === 'Counting';
        body.innerHTML = state.count.lines.length ? state.count.lines.map(line => {
            const counted = line.countedQuantity ?? '';
            const difference = line.countedQuantity == null ? '—' : number(Number(line.countedQuantity) - Number(line.systemQuantity));
            return `
                <tr data-count-line="${line.countLineId}">
                    <td><div class="inventory-product-cell"><img src="${escapeHtml(line.imageUrl)}" onerror="this.src='/images/products/default-product.png'" alt=""><div><strong>${escapeHtml(line.productName)}</strong><small>${escapeHtml(line.sku)} · ${escapeHtml(line.variantLabel)}</small></div></div></td>
                    <td>${number(line.systemQuantity)}</td>
                    <td><input class="inventory-table-input" type="number" min="0" value="${escapeHtml(counted)}" data-count-field="quantity" ${editable ? '' : 'disabled'}></td>
                    <td>${difference}</td>
                    <td><select class="inventory-table-input" data-count-field="reason" ${editable ? '' : 'disabled'}><option value="">Không chênh lệch</option><option value="DAMAGE" ${line.reasonCode === 'DAMAGE' ? 'selected' : ''}>Hư hỏng</option><option value="LOSS" ${line.reasonCode === 'LOSS' ? 'selected' : ''}>Thiếu hàng</option><option value="FOUND" ${line.reasonCode === 'FOUND' ? 'selected' : ''}>Tìm thấy thêm</option><option value="MISCOUNT" ${line.reasonCode === 'MISCOUNT' ? 'selected' : ''}>Sai số lần đếm trước</option><option value="RETURN_TO_STOCK" ${line.reasonCode === 'RETURN_TO_STOCK' ? 'selected' : ''}>Hàng được nhập lại kho</option><option value="DATA_CORRECTION" ${line.reasonCode === 'DATA_CORRECTION' ? 'selected' : ''}>Điều chỉnh dữ liệu cũ</option><option value="OTHER" ${line.reasonCode === 'OTHER' ? 'selected' : ''}>Lý do khác</option></select></td>
                    <td><input class="inventory-table-input" type="number" min="0" step="1000" value="${line.adjustmentUnitCost ?? line.unitCostSnapshot ?? 0}" data-count-field="cost" ${editable ? '' : 'disabled'}></td>
                    <td><input class="inventory-table-input" maxlength="500" value="${escapeHtml(line.note || '')}" data-count-field="note" ${editable ? '' : 'disabled'}></td>
                    <td>${editable ? `<button class="inventory-link-button" type="button" data-save-count-line="${line.countLineId}">Lưu</button>` : ''}</td>
                </tr>`;
        }).join('') : '<tr><td colspan="8" class="inventory-empty">Không có dòng kiểm kê.</td></tr>';
    }

    async function saveCountLine(lineId) {
        const row = document.querySelector(`[data-count-line="${lineId}"]`);
        if (!row) return;
        const countedQuantity = Number(row.querySelector('[data-count-field="quantity"]').value);
        const systemQuantity = Number(state.count.lines.find(item => item.countLineId === lineId)?.systemQuantity || 0);
        const difference = countedQuantity - systemQuantity;
        const reason = row.querySelector('[data-count-field="reason"]').value;
        if (countedQuantity < 0 || !Number.isFinite(countedQuantity)) return toast('Số lượng thực tế không hợp lệ.', 'error');
        if (difference !== 0 && !reason) return toast('Hãy chọn lý do khi số lượng có chênh lệch.', 'error');
        try {
            await postJson(`${api.counting}/${state.count.sessionId}/lines/${lineId}`, {
                countedQuantity,
                reasonCode: difference === 0 ? null : reason,
                adjustmentUnitCost: Number(row.querySelector('[data-count-field="cost"]').value || 0),
                note: row.querySelector('[data-count-field="note"]').value.trim()
            });
            toast('Đã lưu số lượng thực tế.', 'success');
            await openCountSession(state.count.sessionId);
            await loadCountSessions();
        } catch (error) {
            toast(error.message, 'error');
        }
    }

    async function submitCount() {
        if (!confirm('Gửi kết quả kiểm kê để xác nhận?')) return;
        try {
            const result = await postJson(`${api.counting}/${state.count.sessionId}/submit`, {});
            toast(result.message, 'success');
            await Promise.all([openCountSession(state.count.sessionId), loadCountSessions()]);
        } catch (error) { toast(error.message, 'error'); }
    }

    async function postCount() {
        if (!confirm('Hoàn tất kiểm kê và cập nhật số lượng chênh lệch vào kho?')) return;
        try {
            const result = await postJson(`${api.counting}/${state.count.sessionId}/post`, {});
            toast(result.message || 'Đã hoàn tất kiểm kê.', 'success');
            await Promise.all([openCountSession(state.count.sessionId), loadCountSessions(), loadCountProducts(true)]);
        } catch (error) { toast(error.message, 'error'); }
    }

    async function cancelCount() {
        const reason = prompt('Nhập lý do hủy đợt kiểm kê:');
        if (!reason || reason.trim().length < 5) return;
        try {
            const result = await postJson(`${api.counting}/${state.count.sessionId}/cancel`, { reason: reason.trim() });
            toast(result.message || 'Đã hủy đợt kiểm kê.', 'success');
            await Promise.all([openCountSession(state.count.sessionId), loadCountSessions()]);
        } catch (error) { toast(error.message, 'error'); }
    }

    function bindEvents() {
        document.querySelectorAll('[data-operation-tab]').forEach(button => button.addEventListener('click', () => switchTab(button.dataset.operationTab)));

        $('outboundWarehouse').addEventListener('change', () => { state.outbound.lines.clear(); renderOutboundLines(); invalidateOutbound(); loadOutboundProducts(true); });
        $('outboundSearch').addEventListener('input', debounce(() => loadOutboundProducts(true)));
        $('outboundCategory').addEventListener('change', () => loadOutboundProducts(true));
        $('outboundBrand').addEventListener('change', () => loadOutboundProducts(true));
        $('outboundPrevPage').addEventListener('click', () => { if (state.outbound.page > 1) { state.outbound.page--; loadOutboundProducts(); } });
        $('outboundNextPage').addEventListener('click', () => { if (state.outbound.page < state.outbound.totalPages) { state.outbound.page++; loadOutboundProducts(); } });
        $('outboundProductGrid').addEventListener('click', event => { const button = event.target.closest('[data-add-outbound]'); if (button) addOutbound(Number(button.dataset.addOutbound)); });
        $('outboundLineBody').addEventListener('click', event => { const button = event.target.closest('[data-remove-outbound]'); if (!button) return; state.outbound.lines.delete(Number(button.dataset.removeOutbound)); renderOutboundLines(); invalidateOutbound(); loadOutboundProducts(); });
        $('outboundLineBody').addEventListener('change', event => {
            const field = event.target.closest('[data-outbound-field]'); if (!field) return;
            const line = state.outbound.lines.get(Number(field.dataset.id)); if (!line) return;
            let value = Number(field.value || 0);
            if (field.dataset.outboundField === 'quantity') value = Math.max(1, Math.min(line.available, value));
            if (field.dataset.outboundField === 'exportPrice') value = Math.max(1, value);
            line[field.dataset.outboundField] = value;
            renderOutboundLines(); invalidateOutbound();
        });
        ['outboundStore', 'outboundInvoice', 'outboundDiscount', 'outboundShipping', 'outboundNotes'].forEach(id => $(id).addEventListener('input', invalidateOutbound));
        $('clearOutboundButton').addEventListener('click', () => { state.outbound.lines.clear(); renderOutboundLines(); invalidateOutbound(); loadOutboundProducts(); });
        $('checkOutboundButton').addEventListener('click', checkOutbound);
        $('submitOutboundButton').addEventListener('click', submitOutbound);
        $('refreshOutboundRecent').addEventListener('click', loadOutboundRecent);

        $('transferSourceWarehouse').addEventListener('change', () => { keepWarehousesDifferent('source'); state.transfer.lines.clear(); invalidateTransfer(); loadTransferProducts(true); });
        $('transferTargetWarehouse').addEventListener('change', () => { keepWarehousesDifferent('target'); invalidateTransfer(); });
        $('transferSearch').addEventListener('input', debounce(() => loadTransferProducts(true)));
        $('transferPrevPage').addEventListener('click', () => { if (state.transfer.page > 1) { state.transfer.page--; loadTransferProducts(); } });
        $('transferNextPage').addEventListener('click', () => { if (state.transfer.page < state.transfer.totalPages) { state.transfer.page++; loadTransferProducts(); } });
        $('transferProductGrid').addEventListener('click', event => { const button = event.target.closest('[data-add-transfer]'); if (button) addTransfer(Number(button.dataset.addTransfer)); });
        $('transferLineList').addEventListener('click', event => { const button = event.target.closest('[data-remove-transfer]'); if (!button) return; state.transfer.lines.delete(Number(button.dataset.removeTransfer)); invalidateTransfer(); loadTransferProducts(); });
        $('transferLineList').addEventListener('change', event => { const input = event.target.closest('[data-transfer-quantity]'); if (!input) return; const line = state.transfer.lines.get(Number(input.dataset.transferQuantity)); if (!line) return; line.quantity = Math.max(1, Math.min(line.available, Number(input.value || 1))); invalidateTransfer(); });
        $('transferReason').addEventListener('input', invalidateTransfer);
        $('clearTransferButton').addEventListener('click', () => { state.transfer.lines.clear(); invalidateTransfer(); loadTransferProducts(); });
        $('checkTransferButton').addEventListener('click', checkTransfer);
        $('submitTransferButton').addEventListener('click', submitTransfer);
        $('refreshTransferRecent').addEventListener('click', loadTransferRecent);

        $('countWarehouse').addEventListener('change', () => { state.count.selected.clear(); updateCountSelection(); loadCountProducts(true); loadCountSessions(); });
        $('countSearch').addEventListener('input', debounce(() => loadCountProducts(true)));
        $('countPrevPage').addEventListener('click', () => { if (state.count.page > 1) { state.count.page--; loadCountProducts(); } });
        $('countNextPage').addEventListener('click', () => { if (state.count.page < state.count.totalPages) { state.count.page++; loadCountProducts(); } });
        $('countProductGrid').addEventListener('click', event => {
            const button = event.target.closest('[data-toggle-count]'); if (!button) return;
            const id = Number(button.dataset.toggleCount); const item = state.count.products.find(product => product.variantId === id); if (!item) return;
            if (state.count.selected.has(id)) state.count.selected.delete(id); else state.count.selected.set(id, item);
            updateCountSelection();
        });
        $('selectAllCountPageButton').addEventListener('click', () => { state.count.products.forEach(item => state.count.selected.set(item.variantId, item)); updateCountSelection(); });
        $('createCountButton').addEventListener('click', createCount);
        $('refreshCountSessions').addEventListener('click', loadCountSessions);
        $('countSessionBody').addEventListener('click', event => { const button = event.target.closest('[data-open-count]'); if (button) openCountSession(Number(button.dataset.openCount)); });
        $('countLineBody').addEventListener('click', event => { const button = event.target.closest('[data-save-count-line]'); if (button) saveCountLine(Number(button.dataset.saveCountLine)); });
        $('submitCountButton').addEventListener('click', submitCount);
        $('postCountButton').addEventListener('click', postCount);
        $('cancelCountButton').addEventListener('click', cancelCount);
    }

    async function initialize() {
        try {
            await loadBootstrap();
            bindEvents();
            renderOutboundLines();
            renderTransferLines();
            updateCountSelection();
            const requestedTab = new URLSearchParams(window.location.search).get('tab') || 'outbound';
            switchTab(requestedTab);
        } catch (error) {
            toast(error.message, 'error');
        }
    }

    document.addEventListener('DOMContentLoaded', initialize);
})();
