(() => {
    'use strict';

    const api = '/Admin/Inventory/PhaseD/Operations';
    const state = {
        bootstrap: null,
        transferPage: 1,
        transferTotalPages: 1,
        transferProducts: [],
        transferCart: new Map(),
        transferPreviewValid: false,
        transferPreview: null
    };

    const $ = (id) => document.getElementById(id);
    const formatNumber = (value) => new Intl.NumberFormat('vi-VN').format(Number(value || 0));
    const formatMoney = (value) => `${new Intl.NumberFormat('vi-VN', { maximumFractionDigits: 0 }).format(Number(value || 0))} đ`;
    const formatDate = (value) => value ? new Date(value).toLocaleString('vi-VN') : '—';

    function escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    function antiForgeryToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    async function readJson(response) {
        const payload = await response.json().catch(() => ({ success: false, message: 'Phản hồi máy chủ không hợp lệ.' }));
        if (!response.ok || payload.success === false) {
            throw new Error(payload.message || `HTTP ${response.status}`);
        }
        return payload;
    }

    async function getJson(url) {
        return readJson(await fetch(url, { headers: { Accept: 'application/json' } }));
    }

    async function postJson(url, payload) {
        return readJson(await fetch(url, {
            method: 'POST',
            headers: {
                'Content-Type': 'application/json',
                'RequestVerificationToken': antiForgeryToken(),
                Accept: 'application/json'
            },
            body: JSON.stringify(payload ?? {})
        }));
    }

    function toast(message, type = 'info') {
        const node = document.createElement('div');
        node.className = `io-toast ${type}`;
        node.textContent = message;
        $('operationsToastStack')?.appendChild(node);
        window.setTimeout(() => node.remove(), 4200);
    }

    function debounce(fn, delay = 350) {
        let timer;
        return (...args) => {
            window.clearTimeout(timer);
            timer = window.setTimeout(() => fn(...args), delay);
        };
    }

    function fillWarehouseSelect(id, warehouses) {
        const select = $(id);
        if (!select) return;
        select.innerHTML = warehouses.map(item =>
            `<option value="${item.warehouseId}">${escapeHtml(item.warehouseCode)} · ${escapeHtml(item.warehouseName)}${item.isPrimary ? ' (Chính)' : ''}</option>`
        ).join('');
    }

    async function loadBootstrap() {
        const data = await getJson(`${api}/Bootstrap`);
        state.bootstrap = data;
        ['replenishmentWarehouse', 'transferSourceWarehouse', 'transferTargetWarehouse'].forEach(id => fillWarehouseSelect(id, data.warehouses));
        ensureDifferentTransferWarehouses('source');
    }

    function switchTab(tab) {
        document.querySelectorAll('.io-tab').forEach(button => button.classList.toggle('is-active', button.dataset.tab === tab));
        document.querySelectorAll('[data-panel]').forEach(panel => { panel.hidden = panel.dataset.panel !== tab; });
        if (tab === 'replenishment') loadReplenishment();
        if (tab === 'transfer') Promise.all([loadTransferProducts(), loadRecentTransfers()]);
        if (tab === 'health') loadHealth();
    }

    async function loadReplenishment() {
        const body = $('replenishmentTableBody');
        body.innerHTML = '<tr><td class="io-empty" colspan="8">Đang tính kế hoạch bổ sung...</td></tr>';
        const query = new URLSearchParams({
            warehouseId: $('replenishmentWarehouse').value,
            demandWindowDays: $('demandWindowDays').value,
            leadTimeDays: $('leadTimeDays').value,
            safetyDays: $('safetyDays').value,
            fallbackTargetQuantity: $('fallbackTargetQuantity').value
        });
        try {
            const data = await getJson(`${api}/Replenishment?${query}`);
            $('replenishmentSkuCount').textContent = formatNumber(data.suggestionCount);
            $('replenishmentQuantity').textContent = formatNumber(data.totalRecommendedQuantity);
            $('replenishmentValue').textContent = formatMoney(data.estimatedPurchaseValue);
            $('replenishmentPlanningDays').textContent = `${data.planningDays} ngày`;
            $('replenishmentBasis').textContent = data.basis || '';
            if (!data.suggestions.length) {
                body.innerHTML = '<tr><td class="io-empty" colspan="8">Không có SKU cần bổ sung theo tham số hiện tại.</td></tr>';
                return;
            }
            body.innerHTML = data.suggestions.map(item => `
                <tr>
                    <td><div class="io-product-cell"><img src="${escapeHtml(item.imageUrl)}" onerror="this.src='/images/products/default-product.png'" alt=""><div><strong>${escapeHtml(item.productName)}</strong><small>${escapeHtml(item.sku)} · ${escapeHtml(item.variantLabel)}</small></div></div></td>
                    <td><div>On hand: <strong>${formatNumber(item.onHand)}</strong></div><small>Reserved ${formatNumber(item.reserved)} · Available ${formatNumber(item.available)}</small></td>
                    <td><strong>${formatNumber(item.totalDemand)}</strong><small style="display:block;color:#6b7588">Online ${formatNumber(item.onlineDemand)} · Phân phối ${formatNumber(item.distributionDemand)}</small></td>
                    <td>${item.coverageDays == null ? 'Không có cầu' : `${item.coverageDays} ngày`}</td>
                    <td class="io-number">${formatNumber(item.targetQuantity)}</td>
                    <td class="io-number"><strong>${formatNumber(item.recommendedQuantity)}</strong></td>
                    <td class="io-number">${formatMoney(item.estimatedPurchaseValue)}</td>
                    <td><span class="io-badge io-badge-${String(item.priority).toLowerCase()}">${escapeHtml(item.priority)}</span></td>
                </tr>`).join('');
        } catch (error) {
            body.innerHTML = `<tr><td class="io-empty" colspan="8">${escapeHtml(error.message)}</td></tr>`;
            toast(error.message, 'error');
        }
    }

    function ensureDifferentTransferWarehouses(changed) {
        const source = $('transferSourceWarehouse');
        const target = $('transferTargetWarehouse');
        if (!source || !target || source.value !== target.value) return;
        const options = [...target.options];
        if (changed === 'source') {
            const other = options.find(option => option.value !== source.value);
            if (other) target.value = other.value;
        } else {
            const other = [...source.options].find(option => option.value !== target.value);
            if (other) source.value = other.value;
        }
    }

    function transferProductQuery() {
        return new URLSearchParams({
            warehouseId: $('transferSourceWarehouse').value,
            q: $('transferProductSearch').value.trim(),
            stockScope: $('transferStockScope').value,
            page: String(state.transferPage),
            pageSize: '12'
        });
    }

    async function loadTransferProducts(resetPage = false) {
        if (resetPage) state.transferPage = 1;
        const grid = $('transferProductGrid');
        grid.innerHTML = '<div class="io-empty">Đang tải sản phẩm...</div>';
        state.transferPreviewValid = false;
        $('submitTransferButton').disabled = true;
        try {
            const data = await getJson(`${api}/Products?${transferProductQuery()}`);
            state.transferProducts = data.items;
            state.transferTotalPages = data.totalPages;
            state.transferPage = data.page;
            $('transferPageMeta').textContent = `Trang ${data.page} / ${data.totalPages} · ${formatNumber(data.totalItems)} SKU`;
            $('transferPrevPage').disabled = data.page <= 1;
            $('transferNextPage').disabled = data.page >= data.totalPages;
            if (!data.items.length) {
                grid.innerHTML = '<div class="io-empty">Không có sản phẩm phù hợp.</div>';
                return;
            }
            grid.innerHTML = data.items.map(item => `
                <article class="io-product-card">
                    <img src="${escapeHtml(item.imageUrl)}" onerror="this.src='/images/products/default-product.png'" alt="">
                    <div><strong>${escapeHtml(item.productName)}</strong><small>${escapeHtml(item.sku)} · ${escapeHtml(item.variantLabel)}</small><small>${escapeHtml(item.brandName)} · ${escapeHtml(item.categoryName)}</small></div>
                    <div class="io-card-stock"><span>Available <strong>${formatNumber(item.available)}</strong> · Vốn TB ${formatMoney(item.averageCost)}</span><button type="button" data-add-transfer="${item.variantId}" ${item.available <= 0 ? 'disabled' : ''}>Thêm</button></div>
                </article>`).join('');
        } catch (error) {
            grid.innerHTML = `<div class="io-empty">${escapeHtml(error.message)}</div>`;
            toast(error.message, 'error');
        }
    }

    function addTransferLine(variantId) {
        const product = state.transferProducts.find(item => item.variantId === variantId);
        if (!product || product.available <= 0) return;
        const existing = state.transferCart.get(variantId);
        if (existing) {
            existing.quantity = Math.min(product.available, existing.quantity + 1);
            existing.available = product.available;
            existing.averageCost = product.averageCost;
        } else {
            state.transferCart.set(variantId, { ...product, quantity: 1 });
        }
        invalidateTransferPreview();
        renderTransferCart();
    }

    function invalidateTransferPreview() {
        state.transferPreviewValid = false;
        state.transferPreview = null;
        $('submitTransferButton').disabled = true;
        $('transferPreviewResult').hidden = true;
    }

    function renderTransferCart() {
        const container = $('transferCartLines');
        const lines = [...state.transferCart.values()];
        if (!lines.length) {
            container.innerHTML = '<div class="io-empty">Chưa có SKU trong phiếu.</div>';
        } else {
            container.innerHTML = lines.map(item => `
                <div class="io-cart-line" data-transfer-line="${item.variantId}">
                    <div class="io-cart-line-head"><div><strong>${escapeHtml(item.productName)}</strong><small>${escapeHtml(item.sku)} · ${escapeHtml(item.variantLabel)} · Available ${formatNumber(item.available)}</small></div><button type="button" data-remove-transfer="${item.variantId}" title="Xóa"><i class="fa-solid fa-xmark"></i></button></div>
                    <div class="io-cart-line-controls"><label class="io-field"><span>Số lượng</span><input type="number" min="1" max="${item.available}" value="${item.quantity}" data-transfer-quantity="${item.variantId}"></label><strong>${formatMoney(item.quantity * item.averageCost)}</strong></div>
                </div>`).join('');
        }
        $('transferLineCount').textContent = formatNumber(lines.length);
        $('transferTotalQuantity').textContent = formatNumber(lines.reduce((sum, item) => sum + item.quantity, 0));
        $('transferEstimatedValue').textContent = formatMoney(lines.reduce((sum, item) => sum + item.quantity * item.averageCost, 0));
    }

    function transferPayload() {
        return {
            sourceWarehouseId: Number($('transferSourceWarehouse').value),
            targetWarehouseId: Number($('transferTargetWarehouse').value),
            reason: $('transferReason').value.trim(),
            items: [...state.transferCart.values()].map(item => ({ variantId: item.variantId, quantity: Number(item.quantity) }))
        };
    }

    async function previewTransfer() {
        const payload = transferPayload();
        if (!payload.items.length) return toast('Hãy thêm ít nhất một SKU.', 'error');
        if (payload.sourceWarehouseId === payload.targetWarehouseId) return toast('Kho nguồn và kho đích phải khác nhau.', 'error');
        if (payload.reason.length < 5) return toast('Lý do điều chuyển tối thiểu 5 ký tự.', 'error');
        const box = $('transferPreviewResult');
        box.hidden = false;
        box.className = 'io-alert';
        box.textContent = 'Đang kiểm tra tồn khả dụng...';
        try {
            const data = await postJson(`${api}/Transfers/Preview`, payload);
            state.transferPreview = data;
            state.transferPreviewValid = data.success && data.lines.every(item => item.valid);
            box.className = `io-alert ${state.transferPreviewValid ? 'is-success' : 'is-error'}`;
            box.textContent = `${data.message} ${data.lineCount} SKU · ${formatNumber(data.totalQuantity)} sản phẩm · ${formatMoney(data.estimatedValue)}.`;
            $('submitTransferButton').disabled = !state.transferPreviewValid;
        } catch (error) {
            state.transferPreviewValid = false;
            box.className = 'io-alert is-error';
            box.textContent = error.message;
            $('submitTransferButton').disabled = true;
            toast(error.message, 'error');
        }
    }

    async function submitTransfer() {
        if (!state.transferPreviewValid) return toast('Hãy preview lại phiếu trước khi ghi sổ.', 'error');
        if (!window.confirm('Ghi sổ điều chuyển và di chuyển serial giữa hai kho?')) return;
        const button = $('submitTransferButton');
        button.disabled = true;
        try {
            const data = await postJson(`${api}/Transfers/Submit`, transferPayload());
            toast(`${data.transferCode}: ${data.message}`, 'success');
            state.transferCart.clear();
            $('transferReason').value = '';
            invalidateTransferPreview();
            renderTransferCart();
            await Promise.all([loadTransferProducts(true), loadRecentTransfers(), loadReplenishment()]);
        } catch (error) {
            toast(error.message, 'error');
            button.disabled = false;
        }
    }

    async function loadRecentTransfers() {
        const body = $('recentTransferTableBody');
        body.innerHTML = '<tr><td class="io-empty" colspan="6">Đang tải...</td></tr>';
        try {
            const data = await getJson(`${api}/Transfers/Recent?take=15`);
            if (!data.transfers.length) {
                body.innerHTML = '<tr><td class="io-empty" colspan="6">Chưa có điều chuyển Phase D3.</td></tr>';
                return;
            }
            body.innerHTML = data.transfers.map(item => `
                <tr><td><strong>${escapeHtml(item.transferCode)}</strong></td><td>${formatDate(item.transactionDate)}</td><td>${escapeHtml(item.sourceWarehouseName || '—')} → ${escapeHtml(item.targetWarehouseName || '—')}</td><td>${escapeHtml(item.products)}</td><td class="io-number">${formatNumber(item.totalQuantity)}</td><td class="io-number">${formatMoney(item.totalValue)}</td></tr>`).join('');
        } catch (error) {
            body.innerHTML = `<tr><td class="io-empty" colspan="6">${escapeHtml(error.message)}</td></tr>`;
        }
    }

    async function loadHealth() {
        const banner = $('healthBanner');
        banner.className = 'io-health-banner';
        banner.textContent = 'Đang quét dữ liệu tồn kho...';
        try {
            const data = await getJson(`${api}/Health`);
            banner.className = `io-health-banner ${data.healthy ? 'is-healthy' : 'is-danger'}`;
            banner.textContent = data.healthy
                ? `Hệ thống khỏe tại ${formatDate(data.generatedAt)}. Không phát hiện lỗi trọng yếu.`
                : `Phát hiện ${formatNumber(data.criticalIssueCount)} nhóm lỗi cần xử lý. Không tự sửa serial/lô vật lý.`;
            $('healthStockDrift').textContent = formatNumber(data.stockDriftCount);
            $('healthSerialMismatch').textContent = formatNumber(data.serialMismatchCount);
            $('healthInvalidLots').textContent = formatNumber(data.invalidLotCount);
            $('healthAgedLots').textContent = formatNumber(data.agedLotCount);
            $('healthOpenCounts').textContent = formatNumber(data.openCountSessions);
            $('healthReservations').textContent = formatNumber(data.activeReservations);
            renderStockDrifts(data.stockDrifts);
            renderSerialMismatches(data.serialMismatches);
        } catch (error) {
            banner.className = 'io-health-banner is-danger';
            banner.textContent = error.message;
            toast(error.message, 'error');
        }
    }

    function renderStockDrifts(items) {
        const body = $('stockDriftTableBody');
        body.innerHTML = items.length ? items.map(item => `
            <tr><td>SKU-${String(item.variantId).padStart(6, '0')}</td><td>${escapeHtml(item.productName)}</td><td class="io-number">${formatNumber(item.snapshotStock)}</td><td class="io-number">${formatNumber(item.lotStock)}</td><td class="io-number"><strong>${item.difference > 0 ? '+' : ''}${formatNumber(item.difference)}</strong></td></tr>`).join('')
            : '<tr><td class="io-empty" colspan="5">Không có lệch snapshot.</td></tr>';
    }

    function renderSerialMismatches(items) {
        const body = $('serialMismatchTableBody');
        body.innerHTML = items.length ? items.map(item => `
            <tr><td>#${item.lotId}</td><td>SKU-${String(item.variantId).padStart(6, '0')}</td><td>#${item.warehouseId}</td><td class="io-number">${formatNumber(item.remainingQuantity)}</td><td class="io-number">${formatNumber(item.inStockSerialCount)}</td><td class="io-number"><strong>${item.difference > 0 ? '+' : ''}${formatNumber(item.difference)}</strong></td></tr>`).join('')
            : '<tr><td class="io-empty" colspan="6">Không có lệch serial.</td></tr>';
    }

    async function repairSnapshots() {
        const reason = $('repairReason').value.trim();
        if (reason.length < 5) return toast('Nhập lý do tối thiểu 5 ký tự.', 'error');
        if (!window.confirm('Đồng bộ ProductVariants.Stock theo tổng tồn lô hoạt động?')) return;
        try {
            const data = await postJson(`${api}/RepairVariantSnapshots`, { reason });
            toast(data.message, 'success');
            await loadHealth();
        } catch (error) {
            toast(error.message, 'error');
        }
    }

    function bindEvents() {
        document.querySelectorAll('.io-tab').forEach(button => button.addEventListener('click', () => switchTab(button.dataset.tab)));
        $('refreshReplenishmentButton').addEventListener('click', loadReplenishment);
        ['replenishmentWarehouse', 'demandWindowDays', 'leadTimeDays', 'safetyDays', 'fallbackTargetQuantity'].forEach(id => $(id).addEventListener('change', loadReplenishment));

        $('transferSourceWarehouse').addEventListener('change', () => { ensureDifferentTransferWarehouses('source'); state.transferCart.clear(); renderTransferCart(); invalidateTransferPreview(); loadTransferProducts(true); });
        $('transferTargetWarehouse').addEventListener('change', () => { ensureDifferentTransferWarehouses('target'); invalidateTransferPreview(); });
        $('transferStockScope').addEventListener('change', () => loadTransferProducts(true));
        $('transferProductSearch').addEventListener('input', debounce(() => loadTransferProducts(true)));
        $('transferPrevPage').addEventListener('click', () => { if (state.transferPage > 1) { state.transferPage--; loadTransferProducts(); } });
        $('transferNextPage').addEventListener('click', () => { if (state.transferPage < state.transferTotalPages) { state.transferPage++; loadTransferProducts(); } });
        $('transferProductGrid').addEventListener('click', event => {
            const button = event.target.closest('[data-add-transfer]');
            if (button) addTransferLine(Number(button.dataset.addTransfer));
        });
        $('transferCartLines').addEventListener('click', event => {
            const button = event.target.closest('[data-remove-transfer]');
            if (!button) return;
            state.transferCart.delete(Number(button.dataset.removeTransfer));
            invalidateTransferPreview();
            renderTransferCart();
        });
        $('transferCartLines').addEventListener('change', event => {
            const input = event.target.closest('[data-transfer-quantity]');
            if (!input) return;
            const id = Number(input.dataset.transferQuantity);
            const line = state.transferCart.get(id);
            if (!line) return;
            line.quantity = Math.max(1, Math.min(line.available, Number(input.value || 1)));
            input.value = line.quantity;
            invalidateTransferPreview();
            renderTransferCart();
        });
        $('transferReason').addEventListener('input', invalidateTransferPreview);
        $('clearTransferButton').addEventListener('click', () => { state.transferCart.clear(); renderTransferCart(); invalidateTransferPreview(); });
        $('previewTransferButton').addEventListener('click', previewTransfer);
        $('submitTransferButton').addEventListener('click', submitTransfer);
        $('refreshTransfersButton').addEventListener('click', loadRecentTransfers);
        $('refreshHealthButton').addEventListener('click', loadHealth);
        $('repairSnapshotsButton').addEventListener('click', repairSnapshots);
    }

    async function initialize() {
        try {
            await loadBootstrap();
            bindEvents();
            renderTransferCart();
            await loadReplenishment();
        } catch (error) {
            toast(error.message, 'error');
        }
    }

    document.addEventListener('DOMContentLoaded', initialize);
})();
