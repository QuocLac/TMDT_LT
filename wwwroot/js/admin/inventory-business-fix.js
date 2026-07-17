(() => {
    'use strict';

    const action = document.body?.dataset?.kpInventoryAction || '';
    const money = new Intl.NumberFormat('vi-VN');

    function numberValue(id) {
        const element = document.getElementById(id);
        return Number.parseFloat(element?.value || '0') || 0;
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

    function buildSoPayload() {
        if (typeof soItems === 'undefined') return null;
        return {
            storeId: Number.parseInt(document.getElementById('soStore')?.value || '0', 10) || 0,
            fromWarehouseId: Number.parseInt(document.getElementById('soWarehouse')?.value || '0', 10) || 0,
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
            profitElement.textContent = 'Đang tính theo FIFO...';
            profitElement.style.color = '#64748B';
        }

        try {
            const data = await postJson('/Admin/Inventory/EstimateSO', payload);
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
                    `Doanh thu thuần: ${money.format(data.netRevenue)} đ; `
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
            profitLabel.textContent = 'Lợi nhuận theo FIFO (chưa đối soát công nợ):';
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
        document.getElementById('soWarehouse')?.addEventListener('change', scheduleEstimate);
        document.getElementById('soDiscount')?.addEventListener('input', scheduleEstimate);

        // Controller cũ hard-code shippingCost=0. Chỉ sửa payload đúng endpoint SubmitSO.
        const originalFetch = window.fetch.bind(window);
        window.fetch = (input, init = {}) => {
            const url = typeof input === 'string' ? input : input?.url || '';
            if (url.endsWith('/Admin/Inventory/SubmitSO')
                && typeof init.body === 'string') {
                try {
                    const body = JSON.parse(init.body);
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

    async function enhanceIndex() {
        const expectedProfitLabel = Array.from(document.querySelectorAll('div'))
            .find(element => element.textContent?.trim() === 'LỢI NHUẬN DỰ KIẾN GỘP');
        if (expectedProfitLabel) {
            expectedProfitLabel.textContent = 'BIÊN GỘP TIỀM NĂNG CỦA TỒN KHO';
            expectedProfitLabel.title =
                'Giá bán hiện tại của hàng còn tồn trừ giá vốn lô; đây không phải lợi nhuận đã thực hiện.';
        }

        try {
            const query = new URLSearchParams(window.location.search);
            const response = await fetch(`/Admin/Inventory/FinancialOverview?${query.toString()}`);
            const data = await response.json();
            if (!response.ok || data.success === false) return;

            const overviewGrid = document.querySelector(
                'div[style*="grid-template-columns: repeat(4"]');
            if (!overviewGrid) return;

            const cards = [
                {
                    label: 'DOANH THU THUẦN ĐÃ GHI NHẬN',
                    value: `${money.format(data.netRevenueBeforeVat)} đ`,
                    note: 'Không gồm VAT đầu ra'
                },
                {
                    label: 'LỢI NHUẬN FIFO ĐÃ GHI NHẬN',
                    value: `${money.format(data.recognizedProfit)} đ`,
                    note: 'Net revenue - FIFO COGS - shipping cost'
                },
                {
                    label: 'SKU LỆCH TỒN TỔNG / TỒN LÔ',
                    value: String(data.stockMismatchCount),
                    note: data.stockMismatchCount > 0
                        ? 'Cần kiểm kê và đối soát'
                        : 'Dữ liệu đang đồng nhất'
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
        if (!note || document.getElementById('kp-input-vat-deductible')) return;

        const wrapper = document.createElement('label');
        wrapper.style.cssText =
            'display:flex;gap:8px;align-items:flex-start;margin-bottom:12px;padding:10px 12px;border:1px solid #BFDBFE;background:#EFF6FF;border-radius:8px;font-size:12px;color:#1E3A8A;cursor:pointer;';
        wrapper.innerHTML = `
            <input type="checkbox" id="kp-input-vat-deductible" checked style="margin-top:2px;" />
            <span><strong>VAT đầu vào được khấu trừ</strong><br />VAT vẫn nằm trong số tiền phải trả NCC nhưng không vốn hóa vào InventoryLots.UnitCost.</span>`;
        note.closest('div')?.before(wrapper);

        const originalFetch = window.fetch.bind(window);
        window.fetch = (input, init = {}) => {
            const url = typeof input === 'string' ? input : input?.url || '';
            if (url.endsWith('/Admin/Inventory/SubmitPO')
                && typeof init.body === 'string') {
                try {
                    const body = JSON.parse(init.body);
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

    document.addEventListener('DOMContentLoaded', () => {
        if (action === 'CreateSO') enhanceCreateSo();
        if (action === 'Index') enhanceIndex();
        if (action === 'CreatePO') enhanceCreatePo();
    });
})();
