(() => {
    'use strict';

    const action = document.body?.dataset?.kpInventoryAction || '';
    const money = new Intl.NumberFormat('vi-VN');

    function numberValue(id) {
        const element = document.getElementById(id);
        return Number.parseFloat(element?.value || '0') || 0;
    }

    function integerValue(id) {
        return Number.parseInt(document.getElementById(id)?.value || '0', 10) || 0;
    }

    function setMoney(id, value) {
        const element = document.getElementById(id);
        if (element) element.textContent = `${money.format(Number(value || 0))} đ`;
    }

    async function postJson(url, payload) {
        const response = await fetch(url, {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify(payload)
        });
        const data = await response.json();
        if (!response.ok || data.success === false) {
            throw new Error(data.message || `HTTP ${response.status}`);
        }
        return data;
    }

    async function loadWarehouses() {
        const selectors = [
            document.getElementById('poWarehouse'),
            document.getElementById('soWarehouse')
        ].filter(Boolean);
        if (selectors.length === 0) return [];

        try {
            const response = await fetch('/Admin/Inventory/PhaseB/Warehouses');
            const warehouses = await response.json();
            if (!response.ok || !Array.isArray(warehouses)) return [];

            selectors.forEach(select => {
                const selected = Number.parseInt(select.value || '0', 10);
                select.innerHTML = warehouses.map(warehouse =>
                    `<option value="${warehouse.warehouseId}">${warehouse.warehouseName}</option>`
                ).join('');

                if (warehouses.some(item => item.warehouseId === selected)) {
                    select.value = String(selected);
                } else {
                    const primary = warehouses.find(item => item.isPrimary) || warehouses[0];
                    if (primary) select.value = String(primary.warehouseId);
                }
            });

            return warehouses;
        } catch {
            return [];
        }
    }

    function installPhaseBRouting() {
        const originalFetch = window.fetch.bind(window);
        const routeMap = new Map([
            ['/Admin/Inventory/GetAllProductsWithStock', '/Admin/Inventory/PhaseB/GetAllProductsWithStock'],
            ['/Admin/Inventory/SearchVariants', '/Admin/Inventory/PhaseB/SearchVariants'],
            ['/Admin/Inventory/SubmitPO', '/Admin/Inventory/PhaseB/SubmitPO'],
            ['/Admin/Inventory/SubmitSO', '/Admin/Inventory/PhaseB/SubmitSO'],
            ['/Admin/Inventory/AdjustStock', '/Admin/Inventory/PhaseB/AdjustStock'],
            ['/Admin/Inventory/UpdateLotDetail', '/Admin/Inventory/PhaseB/UpdateLotDetail']
        ]);

        window.fetch = (input, init = {}) => {
            let url = typeof input === 'string' ? input : input?.url || '';
            for (const [legacyRoute, phaseBRoute] of routeMap) {
                if (url.startsWith(legacyRoute)) {
                    url = phaseBRoute + url.slice(legacyRoute.length);
                    break;
                }
            }

            if (url.includes('/Admin/Inventory/PhaseB/GetAllProductsWithStock')) {
                const warehouseId = action === 'CreatePO'
                    ? integerValue('poWarehouse')
                    : integerValue('soWarehouse');
                if (warehouseId > 0 && !url.includes('warehouseId=')) {
                    const separator = url.includes('?') ? '&' : '?';
                    url = `${url}${separator}warehouseId=${warehouseId}`;
                }
            }

            input = typeof input === 'string'
                ? url
                : new Request(url, input);
            return originalFetch(input, init);
        };
    }

    function buildSoPayload() {
        if (typeof soItems === 'undefined') return null;
        return {
            storeId: integerValue('soStore'),
            fromWarehouseId: integerValue('soWarehouse'),
            invoiceNumber: document.getElementById('soInvoiceNumber')?.value || '',
            discountPercent: numberValue('soDiscount'),
            shippingCost: numberValue('kp-so-shipping-cost'),
            notes: document.getElementById('soNotes')?.value || '',
            items: soItems.map(item => ({
                variantId: Number.parseInt(item.variantId, 10),
                quantity: Number.parseInt(item.qty, 10),
                exportPrice: Number.parseFloat(item.price),
                taxRate: Number.parseFloat(item.taxRate)
            }))
        };
    }

    let estimateTimer = null;
    let estimateSequence = 0;

    function scheduleEstimate() {
        clearTimeout(estimateTimer);
        estimateTimer = setTimeout(estimateSo, 180);
    }

    async function estimateSo() {
        const payload = buildSoPayload();
        const profitElement = document.getElementById('summaryProfit');
        if (!payload || payload.items.length === 0) {
            if (profitElement) profitElement.textContent = 'Chưa có dữ liệu FIFO';
            return;
        }

        const sequence = ++estimateSequence;
        if (profitElement) {
            profitElement.textContent = 'Đang tính FIFO theo kho...';
            profitElement.style.color = '#64748B';
        }

        try {
            const data = await postJson('/Admin/Inventory/PhaseB/EstimateSO', payload);
            if (sequence !== estimateSequence) return;

            setMoney('summaryDiscount', data.discountAmount);
            setMoney('summaryTaxTotal', data.taxAmount);
            setMoney('summaryTotal', data.invoiceTotal);
            setMoney('summaryProfit', data.realizedProfit);

            if (profitElement) {
                profitElement.style.color = Number(data.realizedProfit) >= 0
                    ? '#10B981'
                    : '#DC2626';
                profitElement.title =
                    `Kho #${data.warehouseId}; doanh thu thuần: ${money.format(data.netRevenue)} đ; `
                    + `FIFO COGS: ${money.format(data.cogs)} đ; `
                    + `chi phí vận chuyển: ${money.format(data.shippingCost)} đ.`;
            }
        } catch (error) {
            if (sequence !== estimateSequence) return;
            if (profitElement) {
                profitElement.textContent = error.message || 'Không tính được FIFO';
                profitElement.style.color = '#DC2626';
            }
        }
    }

    function reloadProductList() {
        if (typeof window.loadProductList === 'function') {
            window.loadProductList();
        }
    }

    function enhanceCreateSo() {
        const notes = document.getElementById('soNotes');
        if (notes && !document.getElementById('kp-so-shipping-cost')) {
            const wrapper = document.createElement('div');
            wrapper.style.marginBottom = '12px';
            wrapper.innerHTML = `
                <label style="display:block;font-size:12px;font-weight:600;margin-bottom:6px;color:#374151;text-transform:uppercase;">
                    Chi phí vận chuyển thực tế doanh nghiệp chịu
                </label>
                <input type="number" id="kp-so-shipping-cost" value="0" min="0"
                       style="width:100%;padding:9px 12px;border-radius:6px;border:1px solid #D1D5DB;font-size:13px;outline:none;" />
                <small style="display:block;margin-top:4px;color:#64748B;line-height:1.5;">
                    Khoản này là chi phí, được trừ khỏi lợi nhuận; không cộng vào doanh thu.
                </small>`;
            notes.closest('div')?.before(wrapper);
            wrapper.querySelector('input')?.addEventListener('input', scheduleEstimate);
        }

        const profitElement = document.getElementById('summaryProfit');
        const profitLabel = profitElement?.previousElementSibling;
        if (profitLabel) {
            profitLabel.textContent = 'Lợi nhuận FIFO của kho đã chọn:';
        }
        if (profitElement) profitElement.textContent = 'Chưa có dữ liệu FIFO';

        const originalRender = window.renderTable;
        if (typeof originalRender === 'function') {
            window.renderTable = function (...args) {
                const result = originalRender.apply(this, args);
                scheduleEstimate();
                return result;
            };
        }

        document.getElementById('soStore')?.addEventListener('change', scheduleEstimate);
        document.getElementById('soWarehouse')?.addEventListener('change', () => {
            reloadProductList();
            scheduleEstimate();
        });
        document.getElementById('soDiscount')?.addEventListener('input', scheduleEstimate);

        const originalFetch = window.fetch.bind(window);
        window.fetch = (input, init = {}) => {
            const url = typeof input === 'string' ? input : input?.url || '';
            if (url.endsWith('/Admin/Inventory/SubmitSO')
                && typeof init.body === 'string') {
                try {
                    const body = JSON.parse(init.body);
                    body.fromWarehouseId = integerValue('soWarehouse');
                    body.shippingCost = numberValue('kp-so-shipping-cost');
                    init = { ...init, body: JSON.stringify(body) };
                } catch {
                    // Giữ nguyên request nếu body không phải JSON hợp lệ.
                }
            }
            return originalFetch(input, init);
        };

        scheduleEstimate();
    }

    async function updateLotLocations() {
        try {
            const [locationResponse, warehouseResponse] = await Promise.all([
                fetch('/Admin/Inventory/PhaseB/LotLocations'),
                fetch('/Admin/Inventory/PhaseB/Warehouses')
            ]);
            const locations = await locationResponse.json();
            const warehouses = await warehouseResponse.json();
            if (!locationResponse.ok || !Array.isArray(locations)) return;
            const map = new Map(locations.map(item => [Number(item.lotId), item]));

            document.querySelectorAll('.lot-data-row').forEach(row => {
                const match = row.id?.match(/lot-row-(\d+)/);
                if (!match) return;
                const lotId = Number(match[1]);
                const location = map.get(lotId);
                if (!location) return;
                const cell = row.querySelector('.lot-store-cell');
                if (cell) cell.textContent = location.warehouseName;
                row.dataset.store = String(location.warehouseName || '').toLowerCase();
                row.dataset.warehouseId = String(location.warehouseId);

                const actionCell = row.querySelector('td:last-child');
                if (!actionCell || actionCell.querySelector('.kp-transfer-lot')) return;
                const button = document.createElement('button');
                button.type = 'button';
                button.className = 'kp-transfer-lot';
                button.textContent = 'Chuyển kho';
                button.style.cssText =
                    'margin-top:6px;padding:5px 8px;border:1px solid #BFDBFE;background:#EFF6FF;color:#1D4ED8;border-radius:6px;font-size:11px;font-weight:700;cursor:pointer;';
                button.addEventListener('click', async () => {
                    const choices = (Array.isArray(warehouses) ? warehouses : [])
                        .filter(item => Number(item.warehouseId) !== Number(location.warehouseId))
                        .map(item => `${item.warehouseId} - ${item.warehouseName}`)
                        .join('\n');
                    const targetRaw = window.prompt(`Chọn ID kho đích:\n${choices}`);
                    if (!targetRaw) return;
                    const quantityRaw = window.prompt('Số lượng chuyển:', '1');
                    if (!quantityRaw) return;
                    const reason = window.prompt('Lý do luân chuyển kho:');
                    if (!reason?.trim()) return;

                    try {
                        const result = await postJson('/Admin/Inventory/PhaseB/TransferLot', {
                            lotId,
                            targetWarehouseId: Number.parseInt(targetRaw, 10),
                            quantity: Number.parseInt(quantityRaw, 10),
                            reason: reason.trim()
                        });
                        window.alert(result.message || 'Luân chuyển kho thành công.');
                        window.location.reload();
                    } catch (error) {
                        window.alert(error.message || 'Không thể luân chuyển kho.');
                    }
                });
                actionCell.appendChild(button);
            });
        } catch {
            // Giữ giao diện cũ nếu endpoint vị trí tạm lỗi.
        }
    }

    async function enhanceIndex() {
        const expectedProfitLabel = Array.from(document.querySelectorAll('div'))
            .find(element => element.textContent?.trim() === 'LỢI NHUẬN DỰ KIẾN GỘP');
        if (expectedProfitLabel) {
            expectedProfitLabel.textContent = 'BIÊN GỘP TIỀM NĂNG CỦA TỒN KHO';
            expectedProfitLabel.title =
                'Giá bán hiện tại của hàng còn tồn trừ giá vốn lô; không phải lợi nhuận đã thực hiện.';
        }

        await updateLotLocations();

        try {
            const query = new URLSearchParams(window.location.search);
            const response = await fetch(`/Admin/Inventory/PhaseB/FinancialOverview?${query.toString()}`);
            const data = await response.json();
            if (!response.ok || data.success === false) return;

            const overviewGrid = document.querySelector(
                'div[style*="grid-template-columns: repeat(4"]');
            if (!overviewGrid) return;

            const cards = [
                {
                    label: 'LỢI NHUẬN FIFO PHÂN PHỐI',
                    value: `${money.format(data.recognizedProfit)} đ`,
                    note: 'Net revenue - FIFO COGS - shipping cost'
                },
                {
                    label: 'TIỀN THỰC THU ĐƠN ONLINE',
                    value: `${money.format(data.onlineNetCashCollected)} đ`,
                    note: `Đã trừ hoàn tiền: ${money.format(data.onlineRefundAmount)} đ`
                },
                {
                    label: 'LỢI NHUẬN THỰC NHẬN TẠM TÍNH',
                    value: `${money.format(data.onlineCashContributionProfit)} đ`,
                    note: 'Chưa trừ phí cổng thanh toán nếu chưa có dữ liệu'
                },
                {
                    label: 'CÔNG NỢ CHƯA THU',
                    value: `${money.format(data.onlineOutstandingAmount)} đ`,
                    note: `${data.onlinePaidOrderCount}/${data.onlineOrderCount} đơn có tiền thu`
                },
                {
                    label: 'SKU LỆCH TỒN TỔNG / TỒN LÔ',
                    value: String(data.stockMismatchCount),
                    note: data.stockMismatchCount > 0
                        ? 'Cần kiểm kê và đối soát'
                        : 'Dữ liệu đang đồng nhất'
                },
                {
                    label: 'LÔ LỆCH TỒN / SERIAL',
                    value: String(data.serialMismatchCount),
                    note: data.serialMismatchCount > 0
                        ? 'Không nên xuất kho trước khi sửa'
                        : 'Serial đang đồng nhất'
                }
            ];

            cards.forEach(card => {
                const element = document.createElement('div');
                element.style.cssText =
                    'background:#F8FAFC;padding:18px;border-radius:10px;border:1px solid #E2E8F0;';
                element.innerHTML = `
                    <div style="color:#64748B;font-size:12px;font-weight:700;margin-bottom:6px;">${card.label}</div>
                    <div style="font-size:21px;font-weight:800;color:#0F172A;">${card.value}</div>
                    <div style="font-size:11px;color:#64748B;margin-top:5px;">${card.note}</div>`;
                overviewGrid.appendChild(element);
            });
        } catch {
            // Dashboard kho vẫn hoạt động nếu endpoint tổng hợp tạm lỗi.
        }
    }

    function enhanceCreatePo() {
        const note = document.getElementById('poNote');
        if (note && !document.getElementById('kp-input-vat-deductible')) {
            const wrapper = document.createElement('label');
            wrapper.style.cssText =
                'display:flex;gap:8px;align-items:flex-start;margin-bottom:12px;padding:10px 12px;border:1px solid #BFDBFE;background:#EFF6FF;border-radius:8px;font-size:12px;color:#1E3A8A;cursor:pointer;';
            wrapper.innerHTML = `
                <input type="checkbox" id="kp-input-vat-deductible" checked style="margin-top:2px;" />
                <span><strong>VAT đầu vào được khấu trừ</strong><br />VAT vẫn nằm trong số tiền phải trả NCC nhưng không vốn hóa vào InventoryLots.UnitCost.</span>`;
            note.closest('div')?.before(wrapper);
        }

        document.getElementById('poWarehouse')?.addEventListener('change', reloadProductList);

        const originalFetch = window.fetch.bind(window);
        window.fetch = (input, init = {}) => {
            const url = typeof input === 'string' ? input : input?.url || '';
            if (url.endsWith('/Admin/Inventory/SubmitPO')
                && typeof init.body === 'string') {
                try {
                    const body = JSON.parse(init.body);
                    body.warehouseId = integerValue('poWarehouse');
                    body.inputVatDeductible =
                        document.getElementById('kp-input-vat-deductible')?.checked !== false;
                    init = { ...init, body: JSON.stringify(body) };
                } catch {
                    // Giữ nguyên request nếu body không phải JSON hợp lệ.
                }
            }
            return originalFetch(input, init);
        };
    }

    document.addEventListener('DOMContentLoaded', async () => {
        installPhaseBRouting();
        await loadWarehouses();

        if (action === 'CreateSO') enhanceCreateSo();
        if (action === 'Index') await enhanceIndex();
        if (action === 'CreatePO') enhanceCreatePo();

        if (action === 'CreateSO' || action === 'CreatePO') {
            reloadProductList();
        }
    });
})();
