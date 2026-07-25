(() => {
    'use strict';

    const operationsApi = '/Admin/Inventory/Operations';
    const distributionApi = '/Admin/Inventory/Distribution';
    const state = {
        bootstrap: null,
        page: 1,
        totalPages: 1,
        products: [],
        lines: new Map(),
        previewValid: false,
        preview: null
    };

    const $ = id => document.getElementById(id);
    const money = value => `${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 }).format(Number(value || 0))} đ`;
    const number = value => new Intl.NumberFormat('vi-VN').format(Number(value || 0));
    const date = value => value ? new Date(value).toLocaleString('vi-VN') : '—';

    function escapeHtml(value) {
        return String(value ?? '').replaceAll('&', '&amp;').replaceAll('<', '&lt;').replaceAll('>', '&gt;').replaceAll('"', '&quot;').replaceAll("'", '&#039;');
    }

    function token() { return document.querySelector('input[name="__RequestVerificationToken"]')?.value || ''; }

    async function readJson(response) {
        const data = await response.json().catch(() => ({ success: false, message: 'Phản hồi máy chủ không hợp lệ.' }));
        if (!response.ok || data.success === false) throw new Error(data.message || `HTTP ${response.status}`);
        return data;
    }

    async function getJson(url) { return readJson(await fetch(url, { headers: { Accept: 'application/json' } })); }
    async function postJson(url, payload) {
        return readJson(await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json', 'RequestVerificationToken': token(), Accept: 'application/json' },
            body: JSON.stringify(payload)
        }));
    }

    function toast(message, type = 'info') {
        const node = document.createElement('div');
        node.className = `id-toast ${type}`;
        node.textContent = message;
        $('distributionToastStack').appendChild(node);
        window.setTimeout(() => node.remove(), 4200);
    }

    function debounce(fn, delay = 350) {
        let timer;
        return (...args) => { clearTimeout(timer); timer = setTimeout(() => fn(...args), delay); };
    }

    function fillSelect(id, items, valueKey, labelBuilder) {
        $(id).innerHTML = items.map(item => `<option value="${item[valueKey]}">${escapeHtml(labelBuilder(item))}</option>`).join('');
    }

    async function loadBootstrap() {
        const data = await getJson(`${operationsApi}/Bootstrap`);
        state.bootstrap = data;
        fillSelect('distributionWarehouse', data.warehouses, 'warehouseId', item => `${item.warehouseCode} · ${item.warehouseName}${item.isPrimary ? ' (Chính)' : ''}`);
        fillSelect('distributionStore', data.stores, 'storeId', item => `${item.storeCode} · ${item.storeName} (${item.storeType})`);
        $('distributionCategory').innerHTML = '<option value="">Tất cả</option>' + data.categories.map(item => `<option value="${item.categoryId}">${escapeHtml(item.categoryName)}</option>`).join('');
        $('distributionBrand').innerHTML = '<option value="">Tất cả</option>' + data.brands.map(item => `<option value="${item.brandId}">${escapeHtml(item.brandName)}</option>`).join('');
    }

    function queryString() {
        return new URLSearchParams({
            warehouseId: $('distributionWarehouse').value,
            q: $('distributionSearch').value.trim(),
            categoryId: $('distributionCategory').value,
            brandId: $('distributionBrand').value,
            stockScope: 'available',
            page: String(state.page),
            pageSize: '12'
        });
    }

    async function loadProducts(reset = false) {
        if (reset) state.page = 1;
        const grid = $('distributionProductGrid');
        grid.innerHTML = '<div class="id-empty">Đang tải sản phẩm...</div>';
        try {
            const data = await getJson(`${operationsApi}/Products?${queryString()}`);
            state.products = data.items;
            state.page = data.page;
            state.totalPages = data.totalPages;
            $('distributionPageMeta').textContent = `Trang ${data.page} / ${data.totalPages} · ${number(data.totalItems)} SKU`;
            $('distributionPrevPage').disabled = data.page <= 1;
            $('distributionNextPage').disabled = data.page >= data.totalPages;
            if (!data.items.length) {
                grid.innerHTML = '<div class="id-empty">Không có SKU khả dụng.</div>';
                return;
            }
            grid.innerHTML = data.items.map(item => `
                <article class="id-product-card">
                    <img src="${escapeHtml(item.imageUrl)}" onerror="this.src='/images/products/default-product.png'" alt="">
                    <div><strong>${escapeHtml(item.productName)}</strong><small>${escapeHtml(item.sku)} · ${escapeHtml(item.variantLabel)}</small><small>${escapeHtml(item.brandName)} · ${escapeHtml(item.categoryName)}</small></div>
                    <div class="id-product-card-footer"><span>Available <strong>${number(item.available)}</strong> · ${money(item.price)}</span><button type="button" data-add-line="${item.variantId}" ${item.available <= 0 ? 'disabled' : ''}>Thêm</button></div>
                </article>`).join('');
        } catch (error) {
            grid.innerHTML = `<div class="id-empty">${escapeHtml(error.message)}</div>`;
            toast(error.message, 'error');
        }
    }

    function addLine(variantId) {
        const product = state.products.find(item => item.variantId === variantId);
        if (!product || product.available <= 0) return;
        const line = state.lines.get(variantId);
        if (line) {
            line.quantity = Math.min(line.available, line.quantity + 1);
            line.available = product.available;
        } else {
            state.lines.set(variantId, {
                ...product,
                quantity: 1,
                exportPrice: Number(product.price || 0),
                taxRate: 0.1
            });
        }
        invalidatePreview();
        renderLines();
    }

    function invalidatePreview() {
        state.previewValid = false;
        state.preview = null;
        $('submitDistributionButton').disabled = true;
        $('distributionPreviewStatus').className = 'id-alert';
        $('distributionPreviewStatus').textContent = 'Dữ liệu đã thay đổi. Hãy preview lại trước khi ghi sổ.';
        ['summaryNetRevenue', 'summaryTax', 'summaryInvoiceTotal', 'summaryCogs', 'summaryShipping', 'summaryProfit', 'summaryMargin'].forEach(id => $(id).textContent = '—');
    }

    function renderLines() {
        const body = $('distributionLineTableBody');
        const lines = [...state.lines.values()];
        if (!lines.length) {
            body.innerHTML = '<tr><td class="id-empty" colspan="7">Chưa có sản phẩm trong phiếu.</td></tr>';
            return;
        }
        body.innerHTML = lines.map(item => `
            <tr data-line="${item.variantId}">
                <td><div class="id-line-product"><img src="${escapeHtml(item.imageUrl)}" onerror="this.src='/images/products/default-product.png'" alt=""><div><strong>${escapeHtml(item.productName)}</strong><small>${escapeHtml(item.sku)} · ${escapeHtml(item.variantLabel)}</small></div></div></td>
                <td>${number(item.available)}</td>
                <td><input class="id-line-input" type="number" min="1" max="${item.available}" value="${item.quantity}" data-line-field="quantity" data-id="${item.variantId}"></td>
                <td><input class="id-line-input" type="number" min="1" step="1000" value="${item.exportPrice}" data-line-field="exportPrice" data-id="${item.variantId}"></td>
                <td><select class="id-line-input" data-line-field="taxRate" data-id="${item.variantId}"><option value="0" ${item.taxRate === 0 ? 'selected' : ''}>0%</option><option value="0.08" ${item.taxRate === 0.08 ? 'selected' : ''}>8%</option><option value="0.1" ${item.taxRate === 0.1 ? 'selected' : ''}>10%</option></select></td>
                <td>${money(item.quantity * item.exportPrice)}</td>
                <td><button class="id-remove-button" type="button" data-remove-line="${item.variantId}"><i class="fa-solid fa-trash"></i></button></td>
            </tr>`).join('');
    }

    function payload() {
        return {
            storeId: Number($('distributionStore').value),
            fromWarehouseId: Number($('distributionWarehouse').value),
            invoiceNumber: $('distributionInvoiceNumber').value.trim(),
            discountPercent: Number($('distributionDiscount').value || 0),
            shippingCost: Number($('distributionShipping').value || 0),
            notes: $('distributionNotes').value.trim(),
            items: [...state.lines.values()].map(item => ({
                variantId: item.variantId,
                quantity: Number(item.quantity),
                exportPrice: Number(item.exportPrice),
                taxRate: Number(item.taxRate)
            }))
        };
    }

    function validatePayload(data) {
        if (!data.storeId || !data.fromWarehouseId) return 'Kho xuất và cửa hàng nhận là bắt buộc.';
        if (!data.items.length) return 'Hãy thêm ít nhất một SKU.';
        if (data.items.some(item => item.quantity <= 0 || item.exportPrice <= 0)) return 'Số lượng và giá bán phải lớn hơn 0.';
        if (data.items.some(item => item.quantity > (state.lines.get(item.variantId)?.available || 0))) return 'Có dòng vượt tồn khả dụng.';
        if (data.discountPercent < 0 || data.discountPercent > 100 || data.shippingCost < 0) return 'Chiết khấu hoặc vận chuyển không hợp lệ.';
        return null;
    }

    async function preview() {
        const data = payload();
        const error = validatePayload(data);
        if (error) return toast(error, 'error');
        const box = $('distributionPreviewStatus');
        box.className = 'id-alert';
        box.textContent = 'Đang lập kế hoạch FIFO và tính tài chính...';
        try {
            const result = await postJson(`${distributionApi}/Preview`, data);
            state.previewValid = true;
            state.preview = result;
            $('summaryNetRevenue').textContent = money(result.netRevenue);
            $('summaryTax').textContent = money(result.taxAmount);
            $('summaryInvoiceTotal').textContent = money(result.invoiceTotal);
            $('summaryCogs').textContent = money(result.cogs);
            $('summaryShipping').textContent = money(result.shippingCost);
            $('summaryProfit').textContent = money(result.realizedProfit);
            $('summaryMargin').textContent = `${Number(result.marginPercent || 0).toLocaleString('vi-VN', { maximumFractionDigits: 2 })}%`;
            box.className = 'id-alert is-success';
            box.textContent = result.accountingBasis || 'Đã preview thành công.';
            $('submitDistributionButton').disabled = false;
        } catch (errorObject) {
            state.previewValid = false;
            box.className = 'id-alert is-error';
            box.textContent = errorObject.message;
            $('submitDistributionButton').disabled = true;
            toast(errorObject.message, 'error');
        }
    }

    async function submit() {
        if (!state.previewValid) return toast('Hãy preview lại trước khi ghi sổ.', 'error');
        if (!window.confirm('Ghi sổ xuất kho FIFO và chuyển serial sang trạng thái Dispatched?')) return;
        const button = $('submitDistributionButton');
        button.disabled = true;
        try {
            const result = await postJson(`${distributionApi}/Submit`, payload());
            toast(`${result.soCode}: xuất kho thành công, lợi nhuận ${money(result.realizedProfit)}.`, 'success');
            state.lines.clear();
            renderLines();
            invalidatePreview();
            $('distributionInvoiceNumber').value = '';
            $('distributionNotes').value = '';
            $('distributionDiscount').value = '0';
            $('distributionShipping').value = '0';
            await Promise.all([loadProducts(true), loadRecent()]);
        } catch (error) {
            toast(error.message, 'error');
            button.disabled = false;
        }
    }

    async function loadRecent() {
        const body = $('recentDistributionTableBody');
        body.innerHTML = '<tr><td class="id-empty" colspan="7">Đang tải...</td></tr>';
        try {
            const data = await getJson(`${distributionApi}/Recent?take=12`);
            if (!data.distributions.length) {
                body.innerHTML = '<tr><td class="id-empty" colspan="7">Chưa có phiếu phân phối.</td></tr>';
                return;
            }
            body.innerHTML = data.distributions.map(item => `
                <tr><td><strong>${escapeHtml(item.soCode)}</strong><small style="display:block;color:#6b7588">${escapeHtml(item.status)}</small></td><td>${date(item.orderDate)}</td><td>${escapeHtml(item.warehouseName)} → ${escapeHtml(item.storeName)}</td><td>${number(item.totalQuantity)} / ${number(item.lineCount)} SKU</td><td>${money(item.totalAmount)}</td><td>${money(item.cogs)}</td><td><strong style="color:${item.profit >= 0 ? '#0f766e' : '#b42318'}">${money(item.profit)}</strong></td></tr>`).join('');
        } catch (error) {
            body.innerHTML = `<tr><td class="id-empty" colspan="7">${escapeHtml(error.message)}</td></tr>`;
        }
    }

    function bindEvents() {
        $('distributionWarehouse').addEventListener('change', () => { state.lines.clear(); renderLines(); invalidatePreview(); loadProducts(true); });
        $('distributionSearch').addEventListener('input', debounce(() => loadProducts(true)));
        $('distributionCategory').addEventListener('change', () => loadProducts(true));
        $('distributionBrand').addEventListener('change', () => loadProducts(true));
        $('distributionPrevPage').addEventListener('click', () => { if (state.page > 1) { state.page--; loadProducts(); } });
        $('distributionNextPage').addEventListener('click', () => { if (state.page < state.totalPages) { state.page++; loadProducts(); } });
        $('distributionProductGrid').addEventListener('click', event => {
            const button = event.target.closest('[data-add-line]');
            if (button) addLine(Number(button.dataset.addLine));
        });
        $('distributionLineTableBody').addEventListener('click', event => {
            const button = event.target.closest('[data-remove-line]');
            if (!button) return;
            state.lines.delete(Number(button.dataset.removeLine));
            renderLines();
            invalidatePreview();
        });
        $('distributionLineTableBody').addEventListener('change', event => {
            const field = event.target.closest('[data-line-field]');
            if (!field) return;
            const line = state.lines.get(Number(field.dataset.id));
            if (!line) return;
            const name = field.dataset.lineField;
            let value = Number(field.value || 0);
            if (name === 'quantity') value = Math.max(1, Math.min(line.available, value));
            if (name === 'exportPrice') value = Math.max(1, value);
            line[name] = value;
            renderLines();
            invalidatePreview();
        });
        ['distributionStore', 'distributionInvoiceNumber', 'distributionDiscount', 'distributionShipping', 'distributionNotes'].forEach(id => $(id).addEventListener('input', invalidatePreview));
        $('clearDistributionButton').addEventListener('click', () => { state.lines.clear(); renderLines(); invalidatePreview(); });
        $('previewDistributionButton').addEventListener('click', preview);
        $('submitDistributionButton').addEventListener('click', submit);
        $('refreshDistributionsButton').addEventListener('click', loadRecent);
    }

    async function initialize() {
        try {
            await loadBootstrap();
            bindEvents();
            renderLines();
            await Promise.all([loadProducts(), loadRecent()]);
        } catch (error) {
            toast(error.message, 'error');
        }
    }

    document.addEventListener('DOMContentLoaded', initialize);
})();
