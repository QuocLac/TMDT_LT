(() => {
    'use strict';

    const api = '/Admin/Inventory';
    const state = {
        warehouses: [],
        warehouseId: 0,
        page: 1,
        pageSize: 20,
        totalPages: 1,
        stockRequest: 0,
        overviewRequest: 0,
        movementChart: null,
        warehouseChart: null
    };

    const byId = id => document.getElementById(id);
    const all = selector => Array.from(document.querySelectorAll(selector));
    const formatNumber = value => new Intl.NumberFormat('vi-VN').format(Number(value || 0));
    const formatMoney = value => `${formatNumber(Math.round(Number(value || 0)))} đ`;
    const formatDateTime = value => {
        if (!value) return '—';
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return '—';
        return new Intl.DateTimeFormat('vi-VN', {
            day: '2-digit', month: '2-digit', year: 'numeric',
            hour: '2-digit', minute: '2-digit'
        }).format(date);
    };

    function escapeHtml(value) {
        return String(value ?? '')
            .replaceAll('&', '&amp;')
            .replaceAll('<', '&lt;')
            .replaceAll('>', '&gt;')
            .replaceAll('"', '&quot;')
            .replaceAll("'", '&#039;');
    }

    async function getJson(url) {
        const response = await fetch(url, {
            headers: { Accept: 'application/json' }
        });
        const payload = await response.json().catch(() => null);
        if (!response.ok || !payload || payload.success === false) {
            throw new Error(payload?.message || `Không thể tải dữ liệu (${response.status}).`);
        }
        return payload;
    }

    function toast(message, isError = false) {
        const stack = byId('inventoryToastStack');
        if (!stack) return;
        const item = document.createElement('div');
        item.className = `inventory-toast${isError ? ' is-error' : ''}`;
        item.textContent = message;
        stack.appendChild(item);
        window.setTimeout(() => item.remove(), 4200);
    }

    function toInputDate(date) {
        const year = date.getFullYear();
        const month = String(date.getMonth() + 1).padStart(2, '0');
        const day = String(date.getDate()).padStart(2, '0');
        return `${year}-${month}-${day}`;
    }

    function setDefaultDates() {
        const today = new Date();
        const from = new Date(today);
        from.setDate(from.getDate() - 29);
        byId('fromDateFilter').value = toInputDate(from);
        byId('toDateFilter').value = toInputDate(today);
    }

    async function loadBootstrap() {
        const data = await getJson(`${api}/Bootstrap`);
        state.warehouses = data.warehouses || [];
        if (state.warehouses.length === 0) {
            throw new Error('Chưa có kho hoạt động.');
        }
        const select = byId('warehouseFilter');
        select.innerHTML = state.warehouses.map(item =>
            `<option value="${item.warehouseId}">${escapeHtml(item.warehouseName)}</option>`
        ).join('');
        const primary = state.warehouses.find(item => item.isPrimary) || state.warehouses[0];
        state.warehouseId = Number(primary.warehouseId);
        select.value = String(state.warehouseId);
        renderWarehouseMeta();
    }

    function renderWarehouseMeta() {
        const warehouse = state.warehouses.find(item => Number(item.warehouseId) === state.warehouseId);
        byId('warehouseMeta').textContent = warehouse
            ? `${warehouse.warehouseCode}${warehouse.address ? ` · ${warehouse.address}` : ''}`
            : '';
    }

    function overviewQuery() {
        return new URLSearchParams({
            warehouseId: String(state.warehouseId),
            fromDate: byId('fromDateFilter').value,
            toDate: byId('toDateFilter').value
        }).toString();
    }

    function stockQuery() {
        const params = new URLSearchParams({
            warehouseId: String(state.warehouseId),
            page: String(state.page),
            pageSize: String(state.pageSize),
            stockScope: byId('stockScopeFilter').value || 'all'
        });
        const keyword = byId('stockSearch').value.trim();
        if (keyword) params.set('q', keyword);
        return params.toString();
    }

    function setText(id, value) {
        const element = byId(id);
        if (element) element.textContent = value;
    }

    function renderOverviewLoading() {
        [
            'actualCashReceived', 'totalInventoryValue', 'tentativeProfit',
            'totalAvailable', 'inboundSpend', 'soldStockCost', 'outstandingAmount',
            'lowStockCount', 'agedStockCount'
        ].forEach(id => setText(id, '—'));
    }

    async function loadOverview() {
        const requestId = ++state.overviewRequest;
        renderOverviewLoading();
        try {
            const data = await getJson(`${api}/Overview?${overviewQuery()}`);
            if (requestId !== state.overviewRequest) return;
            renderOverview(data);
        } catch (error) {
            if (requestId !== state.overviewRequest) return;
            toast(error.message, true);
        }
    }

    function renderOverview(data) {
        const summary = data.summary || {};
        setText('actualCashReceived', formatMoney(summary.actualCashReceived));
        setText('totalInventoryValue', formatMoney(summary.totalInventoryValue));
        setText('tentativeProfit', formatMoney(summary.tentativeProfit));
        setText('totalAvailable', formatNumber(summary.totalAvailable));
        setText('inboundSpend', formatMoney(summary.inboundSpend));
        setText('soldStockCost', formatMoney(summary.soldStockCost));
        setText('outstandingAmount', formatMoney(summary.outstandingAmount));
        setText('lowStockCount', formatNumber(summary.lowStockCount));
        setText('agedStockCount', formatNumber(summary.agedStockCount));
        setText('financeScopeNote', `${data.financeScope || ''}${data.financeNote ? ` ${data.financeNote}` : ''}`.trim());

        renderMovementChart(data.movement || []);
        renderWarehouseChart(data.warehouseValues || []);
        renderTopProducts(data.topProducts || []);
        renderAttention(data.lowStock || [], data.agedStock || []);
        renderRecentActivity(data.recentActivity || []);
    }

    function renderMovementChart(rows) {
        if (state.movementChart) state.movementChart.destroy();
        state.movementChart = new Chart(byId('stockMovementChart'), {
            type: 'line',
            data: {
                labels: rows.map(item => item.label),
                datasets: [
                    {
                        label: 'Nhập kho',
                        data: rows.map(item => item.inbound),
                        borderColor: '#111827',
                        backgroundColor: 'rgba(17,24,39,.08)',
                        tension: .28,
                        fill: true
                    },
                    {
                        label: 'Xuất kho',
                        data: rows.map(item => item.outbound),
                        borderColor: '#9ca3af',
                        backgroundColor: 'rgba(156,163,175,.06)',
                        tension: .28,
                        fill: false
                    }
                ]
            },
            options: chartOptions(false)
        });
    }

    function renderWarehouseChart(rows) {
        if (state.warehouseChart) state.warehouseChart.destroy();
        state.warehouseChart = new Chart(byId('warehouseValueChart'), {
            type: 'bar',
            data: {
                labels: rows.map(item => item.warehouseName),
                datasets: [{
                    label: 'Giá trị tồn',
                    data: rows.map(item => item.inventoryValue),
                    backgroundColor: '#111827',
                    borderRadius: 6
                }]
            },
            options: chartOptions(true)
        });
    }

    function chartOptions(moneyAxis) {
        return {
            responsive: true,
            maintainAspectRatio: false,
            plugins: {
                legend: {
                    labels: { usePointStyle: true, boxWidth: 8, font: { family: 'Inter' } }
                },
                tooltip: moneyAxis ? {
                    callbacks: { label: context => formatMoney(context.raw) }
                } : {}
            },
            scales: {
                x: { grid: { display: false }, ticks: { maxRotation: 0, autoSkip: true, maxTicksLimit: 10 } },
                y: {
                    beginAtZero: true,
                    grid: { color: '#f1f5f9' },
                    ticks: moneyAxis ? { callback: value => `${formatNumber(value / 1000000)}tr` } : {}
                }
            }
        };
    }

    function productCell(item) {
        return `
            <div class="inventory-product">
                <img src="${escapeHtml(item.imageUrl || '/images/products/default-product.png')}" alt="" onerror="this.src='/images/products/default-product.png'" />
                <div>
                    <strong>${escapeHtml(item.productName)}</strong>
                    <small>${escapeHtml(item.variantLabel || item.sku || '')}</small>
                </div>
            </div>`;
    }

    function renderTopProducts(rows) {
        const body = byId('topProductTableBody');
        if (!rows.length) {
            body.innerHTML = '<tr><td colspan="4" class="inventory-empty">Chưa có sản phẩm phát sinh trong kỳ.</td></tr>';
            return;
        }
        body.innerHTML = rows.map(item => `
            <tr>
                <td>${productCell(item)}</td>
                <td class="is-number"><strong>${formatNumber(item.quantity)}</strong></td>
                <td class="is-number">${formatMoney(item.revenue)}</td>
                <td class="is-number"><strong>${formatMoney(item.profit)}</strong></td>
            </tr>`).join('');
    }

    function renderAttention(lowRows, agedRows) {
        const low = byId('lowStockList');
        const aged = byId('agedStockList');
        low.innerHTML = lowRows.length
            ? lowRows.map(item => `
                <div class="inventory-attention-item">
                    <div><strong>${escapeHtml(item.productName)}</strong><small>${escapeHtml(item.variantLabel)}</small></div>
                    <div class="inventory-attention-value">Còn ${formatNumber(item.available)}</div>
                </div>`).join('')
            : '<div class="inventory-empty">Không có mặt hàng sắp hết.</div>';
        aged.innerHTML = agedRows.length
            ? agedRows.map(item => `
                <div class="inventory-attention-item">
                    <div><strong>${escapeHtml(item.productName)}</strong><small>${escapeHtml(item.variantLabel)} · ${formatMoney(item.inventoryValue)}</small></div>
                    <div class="inventory-attention-value">${formatNumber(item.ageDays)} ngày</div>
                </div>`).join('')
            : '<div class="inventory-empty">Không có mặt hàng tồn lâu.</div>';
    }

    function activityLabel(type) {
        const labels = {
            IN_PURCHASE: 'Nhập hàng',
            IN_PURCHASE_RECEIPT: 'Nhập hàng',
            OUT_ORDER: 'Xuất cho đơn hàng',
            OUT_ORDER_FIFO: 'Xuất cho đơn hàng',
            OUT_DISTRIBUTION_FIFO: 'Xuất phân phối',
            IN_TRANSFER_FIFO: 'Nhận chuyển kho',
            OUT_TRANSFER_FIFO: 'Chuyển sang kho khác',
            COUNT_GAIN: 'Tăng sau kiểm kê',
            COUNT_LOSS: 'Giảm sau kiểm kê',
            IN_ORDER_RETURN: 'Nhận hàng hoàn',
            SNAPSHOT_RECONCILIATION: 'Đồng bộ tồn tổng'
        };
        return labels[type] || 'Cập nhật kho';
    }

    function renderRecentActivity(rows) {
        const body = byId('recentActivityTableBody');
        if (!rows.length) {
            body.innerHTML = '<tr><td colspan="5" class="inventory-empty">Chưa có hoạt động kho.</td></tr>';
            return;
        }
        body.innerHTML = rows.map(item => `
            <tr>
                <td>${formatDateTime(item.transactionDate)}</td>
                <td><strong>${escapeHtml(item.productName)}</strong><br><small>${escapeHtml(item.sku)}</small></td>
                <td>${escapeHtml(activityLabel(item.transactionType))}</td>
                <td class="is-number"><strong>${Number(item.quantity) > 0 ? '+' : ''}${formatNumber(item.quantity)}</strong></td>
                <td>${escapeHtml(item.note || '—')}</td>
            </tr>`).join('');
    }

    function renderStockLoading() {
        byId('stockTableBody').innerHTML = Array.from({ length: 6 }, () =>
            '<tr><td colspan="7"><div class="inventory-loading-line"></div></td></tr>'
        ).join('');
    }

    async function loadStock() {
        const requestId = ++state.stockRequest;
        renderStockLoading();
        try {
            const data = await getJson(`${api}/Stock?${stockQuery()}`);
            if (requestId !== state.stockRequest) return;
            state.page = Number(data.page || 1);
            state.totalPages = Number(data.totalPages || 1);
            renderStock(data);
        } catch (error) {
            if (requestId !== state.stockRequest) return;
            byId('stockTableBody').innerHTML = `<tr><td colspan="7" class="inventory-empty">${escapeHtml(error.message)}</td></tr>`;
        }
    }

    function stockStatus(item) {
        const available = Number(item.available || 0);
        if (available <= 0) return { label: 'Hết hàng', className: 'is-danger' };
        if (available <= 5) return { label: 'Sắp hết', className: 'is-warning' };
        if (Number(item.oldestStockDays || 0) >= 90) return { label: 'Tồn lâu', className: 'is-warning' };
        return { label: 'Ổn định', className: 'is-good' };
    }

    function renderStock(data) {
        const rows = data.items || [];
        setText('stockResultMeta', `${formatNumber(data.totalItems)} biến thể trong kho đã chọn`);
        setText('pageIndicator', `Trang ${state.page} / ${state.totalPages}`);
        byId('previousPageButton').disabled = state.page <= 1;
        byId('nextPageButton').disabled = state.page >= state.totalPages;

        if (!rows.length) {
            byId('stockTableBody').innerHTML = '<tr><td colspan="7" class="inventory-empty">Không có sản phẩm phù hợp.</td></tr>';
            return;
        }

        byId('stockTableBody').innerHTML = rows.map(item => {
            const status = stockStatus(item);
            return `
                <tr>
                    <td>${productCell(item)}</td>
                    <td class="is-number">${formatNumber(item.onHand)}</td>
                    <td class="is-number">${formatNumber(item.reserved)}</td>
                    <td class="is-number"><strong>${formatNumber(item.available)}</strong></td>
                    <td class="is-number">${formatMoney(item.averageCost)}</td>
                    <td class="is-number">${formatMoney(item.inventoryValue)}</td>
                    <td><span class="inventory-status ${status.className}">${status.label}</span></td>
                </tr>`;
        }).join('');
    }

    async function refreshAll() {
        await Promise.all([loadOverview(), loadStock()]);
    }

    function bindEvents() {
        byId('warehouseFilter').addEventListener('change', async event => {
            state.warehouseId = Number(event.currentTarget.value);
            state.page = 1;
            renderWarehouseMeta();
            await refreshAll();
        });
        byId('applyOverviewFilterButton').addEventListener('click', loadOverview);

        let searchTimer = null;
        byId('stockSearch').addEventListener('input', () => {
            window.clearTimeout(searchTimer);
            searchTimer = window.setTimeout(() => {
                state.page = 1;
                loadStock();
            }, 320);
        });
        byId('stockScopeFilter').addEventListener('change', () => {
            state.page = 1;
            loadStock();
        });
        byId('previousPageButton').addEventListener('click', () => {
            if (state.page <= 1) return;
            state.page -= 1;
            loadStock();
        });
        byId('nextPageButton').addEventListener('click', () => {
            if (state.page >= state.totalPages) return;
            state.page += 1;
            loadStock();
        });

        all('[data-attention-tab]').forEach(button => {
            button.addEventListener('click', () => {
                all('[data-attention-tab]').forEach(item => item.classList.remove('is-active'));
                button.classList.add('is-active');
                const tab = button.dataset.attentionTab;
                byId('lowStockList').hidden = tab !== 'low';
                byId('agedStockList').hidden = tab !== 'aged';
            });
        });
    }

    async function initialize() {
        try {
            setDefaultDates();
            await loadBootstrap();
            bindEvents();
            await refreshAll();
        } catch (error) {
            toast(error.message, true);
        }
    }

    document.addEventListener('DOMContentLoaded', initialize);
})();
