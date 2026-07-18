(() => {
    'use strict';

    const apiBase = '/Admin/Inventory/PhaseD/Control';
    const state = {
        bootstrap: null,
        warehouseId: 0,
        page: 1,
        pageSize: 20,
        totalPages: 1,
        totalItems: 0,
        stockItems: [],
        selectedVariantIds: new Set(),
        sessions: [],
        transactions: [],
        currentSession: null,
        dirtyLineIds: new Set(),
        loadingStockSequence: 0
    };

    const byId = id => document.getElementById(id);
    const all = selector => Array.from(document.querySelectorAll(selector));
    const formatNumber = value => new Intl.NumberFormat('vi-VN').format(Number(value || 0));
    const formatMoney = value => `${formatNumber(Math.round(Number(value || 0)))} đ`;
    const formatDateTime = value => {
        if (!value) return '—';
        const date = new Date(value);
        return Number.isNaN(date.getTime())
            ? '—'
            : new Intl.DateTimeFormat('vi-VN', {
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

    function antiForgeryToken() {
        return document.querySelector('input[name="__RequestVerificationToken"]')?.value || '';
    }

    async function readJson(response) {
        const data = await response.json().catch(() => null);
        if (!response.ok || !data || data.success === false) {
            const error = new Error(data?.message || `Yêu cầu thất bại (HTTP ${response.status}).`);
            error.payload = data;
            error.status = response.status;
            throw error;
        }
        return data;
    }

    async function getJson(url) {
        const response = await fetch(url, {
            method: 'GET',
            headers: { Accept: 'application/json' }
        });
        return readJson(response);
    }

    async function postJson(url, payload = {}) {
        const response = await fetch(url, {
            method: 'POST',
            headers: {
                Accept: 'application/json',
                'Content-Type': 'application/json',
                RequestVerificationToken: antiForgeryToken()
            },
            body: JSON.stringify(payload)
        });
        return readJson(response);
    }

    function toast(message, type = 'info') {
        const stack = byId('toastStack');
        if (!stack) return;
        const item = document.createElement('div');
        item.className = `ic-toast ${type}`;
        item.textContent = message;
        stack.appendChild(item);
        window.setTimeout(() => item.remove(), 4500);
    }

    function openModal(id) {
        const modal = byId(id);
        if (!modal) return;
        modal.hidden = false;
        document.body.style.overflow = 'hidden';
    }

    function closeModal(id) {
        const modal = byId(id);
        if (!modal) return;
        modal.hidden = true;
        if (!document.querySelector('.ic-modal:not([hidden])')) {
            document.body.style.overflow = '';
        }
    }

    function statusPresentation(status) {
        const map = {
            Counting: { label: 'Đang kiểm đếm', className: 'ic-badge-info' },
            PendingApproval: { label: 'Chờ duyệt', className: 'ic-badge-warning' },
            Posted: { label: 'Đã ghi sổ', className: 'ic-badge-success' },
            Cancelled: { label: 'Đã hủy', className: 'ic-badge-neutral' }
        };
        return map[status] || { label: status || 'Không rõ', className: 'ic-badge-neutral' };
    }

    function transactionPresentation(type) {
        const map = {
            COUNT_GAIN: { label: 'Tăng do kiểm kê', className: 'ic-badge-success' },
            COUNT_LOSS: { label: 'Giảm do kiểm kê', className: 'ic-badge-danger' },
            IN_PURCHASE: { label: 'Nhập từ NCC', className: 'ic-badge-info' },
            IN_PURCHASE_RECEIPT: { label: 'Nhận hàng NCC', className: 'ic-badge-info' },
            OUT_ORDER: { label: 'Xuất đơn hàng', className: 'ic-badge-warning' },
            OUT_ORDER_FIFO: { label: 'Xuất FIFO đơn hàng', className: 'ic-badge-warning' },
            IN_ORDER_RETURN: { label: 'Hoàn tồn đơn hàng', className: 'ic-badge-success' },
            ADJUST: { label: 'Điều chỉnh cũ', className: 'ic-badge-neutral' }
        };
        return map[type] || { label: type || 'Khác', className: 'ic-badge-neutral' };
    }

    function reasonLabel(code) {
        return state.bootstrap?.reasons?.find(item => item.code === code)?.label || code || '—';
    }

    function fillWarehouseSelect(selectId, warehouses) {
        const select = byId(selectId);
        if (!select) return;
        select.innerHTML = warehouses.map(item =>
            `<option value="${item.warehouseId}">${escapeHtml(item.warehouseCode)} · ${escapeHtml(item.warehouseName)}</option>`
        ).join('');
    }

    async function loadBootstrap() {
        const data = await getJson(`${apiBase}/Bootstrap`);
        state.bootstrap = data;
        const warehouses = data.warehouses || [];
        if (warehouses.length === 0) {
            throw new Error('Chưa có kho hoạt động.');
        }
        fillWarehouseSelect('warehouseFilter', warehouses);
        fillWarehouseSelect('countWarehouseId', warehouses);
        const primary = warehouses.find(item => item.isPrimary) || warehouses[0];
        state.warehouseId = Number(primary.warehouseId);
        byId('warehouseFilter').value = String(state.warehouseId);
        byId('countWarehouseId').value = String(state.warehouseId);
        updateWarehouseMeta();
    }

    function updateWarehouseMeta() {
        const warehouse = state.bootstrap?.warehouses?.find(
            item => Number(item.warehouseId) === Number(state.warehouseId));
        byId('warehouseMeta').textContent = warehouse
            ? `${warehouse.warehouseCode} · ${warehouse.address || 'Chưa có địa chỉ'}${warehouse.isPrimary ? ' · Kho chính' : ''}`
            : 'Chưa chọn kho.';
    }

    function stockQueryString() {
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

    function renderStockLoading() {
        byId('stockTableBody').innerHTML = Array.from({ length: 7 }, () => `
            <tr class="ic-loading-row">
                <td colspan="9"><div class="ic-skeleton-line"></div></td>
            </tr>`).join('');
    }

    async function loadStock() {
        const sequence = ++state.loadingStockSequence;
        renderStockLoading();
        try {
            const data = await getJson(`${apiBase}/Stock?${stockQueryString()}`);
            if (sequence !== state.loadingStockSequence) return;
            state.stockItems = data.items || [];
            state.page = Number(data.page || 1);
            state.totalPages = Number(data.totalPages || 1);
            state.totalItems = Number(data.totalItems || 0);
            renderStock(data);
        } catch (error) {
            if (sequence !== state.loadingStockSequence) return;
            renderStockError(error);
        }
    }

    function renderStockError(error) {
        const migrationHint = String(error.message || '').toLowerCase().includes('invalid object name')
            ? ' Chưa có bảng kiểm kê D2; hãy tạo và chạy migration theo tài liệu trong ZIP.'
            : '';
        byId('stockTableBody').innerHTML = `
            <tr class="ic-empty-row"><td colspan="9">${escapeHtml((error.message || 'Không tải được tồn kho.') + migrationHint)}</td></tr>`;
        byId('stockResultMeta').textContent = 'Không thể tải dữ liệu';
        renderSummary({});
    }

    function renderSummary(summary) {
        byId('totalOnHand').textContent = formatNumber(summary.totalOnHand);
        byId('totalReserved').textContent = formatNumber(summary.totalReserved);
        byId('totalAvailable').textContent = formatNumber(summary.totalAvailable);
        byId('totalInventoryValue').textContent = formatMoney(summary.totalInventoryValue);
        byId('lowStockCount').textContent = formatNumber(summary.lowStockCount);
        byId('pendingCountCount').textContent = formatNumber(summary.pendingCounts);
    }

    function stockQuantityClass(quantity) {
        const value = Number(quantity || 0);
        if (value <= 0) return 'danger';
        if (value <= 5) return 'warning';
        return 'available';
    }

    function ageBadge(days) {
        const value = Number(days || 0);
        if (value <= 0) return '<span class="ic-badge ic-badge-neutral">Chưa có tồn</span>';
        if (value >= 90) return `<span class="ic-badge ic-badge-danger">${value} ngày</span>`;
        if (value >= 30) return `<span class="ic-badge ic-badge-warning">${value} ngày</span>`;
        return `<span class="ic-badge ic-badge-success">${value} ngày</span>`;
    }

    function renderStock(data) {
        renderSummary(data.summary || {});
        const rows = state.stockItems;
        byId('stockResultMeta').textContent = `${formatNumber(state.totalItems)} biến thể · ${data.reservationNote || ''}`;
        byId('pageIndicator').textContent = `Trang ${state.page} / ${state.totalPages}`;
        byId('previousPageButton').disabled = state.page <= 1;
        byId('nextPageButton').disabled = state.page >= state.totalPages;

        if (rows.length === 0) {
            byId('stockTableBody').innerHTML = '<tr class="ic-empty-row"><td colspan="9">Không có SKU phù hợp với bộ lọc.</td></tr>';
            updateSelectionUi();
            return;
        }

        byId('stockTableBody').innerHTML = rows.map(item => {
            const selected = state.selectedVariantIds.has(Number(item.variantId));
            const drift = Number(item.stockDrift || 0);
            return `
                <tr data-variant-id="${item.variantId}">
                    <td class="ic-checkbox-column">
                        <input class="stock-row-checkbox" type="checkbox" value="${item.variantId}" ${selected ? 'checked' : ''} aria-label="Chọn ${escapeHtml(item.sku)}" />
                    </td>
                    <td>
                        <div class="ic-product-cell">
                            <img src="${escapeHtml(item.imageUrl)}" alt="" onerror="this.src='/images/products/default-product.png'" />
                            <div>
                                <span class="ic-product-name">${escapeHtml(item.productName)}</span>
                                <span class="ic-product-meta"><span class="ic-sku">${escapeHtml(item.sku)}</span> · ${escapeHtml(item.variantLabel)}</span>
                                <span class="ic-product-meta">${escapeHtml(item.brandName)} · ${escapeHtml(item.categoryName)}</span>
                            </div>
                        </div>
                    </td>
                    <td class="ic-number-column"><span class="ic-quantity ${stockQuantityClass(item.onHand)}">${formatNumber(item.onHand)}</span></td>
                    <td class="ic-number-column">${formatNumber(item.reserved)}</td>
                    <td class="ic-number-column"><span class="ic-quantity ${stockQuantityClass(item.available)}">${formatNumber(item.available)}</span></td>
                    <td class="ic-number-column">${formatMoney(item.averageCost)}</td>
                    <td class="ic-number-column">${formatMoney(item.inventoryValue)}</td>
                    <td>${ageBadge(item.oldestStockDays)}</td>
                    <td>
                        <span class="ic-badge ${drift === 0 ? 'ic-badge-success' : 'ic-badge-danger'}">
                            ${drift === 0 ? 'Đã đồng bộ' : `Lệch ${drift > 0 ? '+' : ''}${formatNumber(drift)}`}
                        </span>
                        ${drift === 0 ? '' : `<div class="ic-drift-note">Lots ${formatNumber(item.aggregateOnHand)} · Snapshot ${formatNumber(item.aggregateStockSnapshot)}</div>`}
                    </td>
                </tr>`;
        }).join('');

        all('.stock-row-checkbox').forEach(checkbox => {
            checkbox.addEventListener('change', event => {
                const variantId = Number(event.currentTarget.value);
                if (event.currentTarget.checked) state.selectedVariantIds.add(variantId);
                else state.selectedVariantIds.delete(variantId);
                updateSelectionUi();
            });
        });
        updateSelectionUi();
    }

    function updateSelectionUi() {
        byId('selectedSkuCount').textContent = `${formatNumber(state.selectedVariantIds.size)} SKU đã chọn`;
        const pageIds = state.stockItems.map(item => Number(item.variantId));
        const checkedOnPage = pageIds.filter(id => state.selectedVariantIds.has(id)).length;
        const selectPage = byId('selectPageCheckbox');
        selectPage.checked = pageIds.length > 0 && checkedOnPage === pageIds.length;
        selectPage.indeterminate = checkedOnPage > 0 && checkedOnPage < pageIds.length;
        byId('countSelectionInfo').textContent = state.selectedVariantIds.size > 0
            ? `Phiên kiểm kê chu kỳ sẽ chụp snapshot ${formatNumber(state.selectedVariantIds.size)} SKU đã chọn.`
            : 'Chưa chọn SKU. Chọn “Kiểm kê toàn kho” hoặc đóng cửa sổ và chọn SKU trong bảng.';
    }

    async function loadSessions() {
        const params = new URLSearchParams({ warehouseId: String(state.warehouseId), take: '50' });
        const status = byId('sessionStatusFilter').value;
        if (status) params.set('status', status);
        try {
            const data = await getJson(`${apiBase}/Sessions?${params}`);
            state.sessions = data.sessions || [];
            renderSessions();
        } catch (error) {
            byId('sessionTableBody').innerHTML = `<tr class="ic-empty-row"><td colspan="9">${escapeHtml(error.message)}</td></tr>`;
        }
    }

    function renderSessions() {
        const body = byId('sessionTableBody');
        if (state.sessions.length === 0) {
            body.innerHTML = '<tr class="ic-empty-row"><td colspan="9">Chưa có phiên kiểm kê phù hợp.</td></tr>';
            return;
        }
        body.innerHTML = state.sessions.map(item => {
            const status = statusPresentation(item.status);
            const progress = item.lineCount > 0 ? Math.round((item.countedLineCount / item.lineCount) * 100) : 0;
            return `
                <tr>
                    <td><button type="button" class="ic-link-button open-session-button" data-session-id="${item.countSessionId}">${escapeHtml(item.countCode)}</button></td>
                    <td>${escapeHtml(item.warehouseCode)} · ${escapeHtml(item.warehouseName)}</td>
                    <td><span class="ic-badge ic-badge-neutral">${item.scopeType === 'Full' ? 'Toàn kho' : 'Chu kỳ'}</span></td>
                    <td>${formatNumber(item.countedLineCount)} / ${formatNumber(item.lineCount)} <span class="ic-product-meta">${progress}%</span></td>
                    <td>${formatNumber(item.discrepancyLineCount)} dòng</td>
                    <td class="ic-number-column">${formatMoney(item.varianceValue)}</td>
                    <td><span class="ic-badge ${status.className}">${status.label}</span></td>
                    <td>${formatDateTime(item.createdAt)}</td>
                    <td><button type="button" class="ic-button ic-button-light open-session-button" data-session-id="${item.countSessionId}">Mở</button></td>
                </tr>`;
        }).join('');
        all('.open-session-button').forEach(button => button.addEventListener('click', () => openSession(Number(button.dataset.sessionId))));
    }

    async function loadTransactions() {
        try {
            const data = await getJson(`${apiBase}/Transactions?warehouseId=${state.warehouseId}&take=30`);
            state.transactions = data.transactions || [];
            renderTransactions();
        } catch (error) {
            byId('transactionTableBody').innerHTML = `<tr class="ic-empty-row"><td colspan="7">${escapeHtml(error.message)}</td></tr>`;
        }
    }

    function renderTransactions() {
        const body = byId('transactionTableBody');
        if (state.transactions.length === 0) {
            body.innerHTML = '<tr class="ic-empty-row"><td colspan="7">Chưa có giao dịch D2 theo kho này.</td></tr>';
            return;
        }
        body.innerHTML = state.transactions.map(item => {
            const type = transactionPresentation(item.transactionType);
            const quantity = Number(item.quantity || 0);
            return `
                <tr>
                    <td>${formatDateTime(item.transactionDate)}</td>
                    <td><strong>${escapeHtml(item.productName)}</strong><span class="ic-product-meta ic-sku">${escapeHtml(item.sku)}</span></td>
                    <td><span class="ic-badge ${type.className}">${type.label}</span></td>
                    <td class="ic-number-column">${item.quantityBefore ?? '—'} → ${item.quantityAfter ?? '—'}</td>
                    <td class="ic-number-column"><span class="${quantity > 0 ? 'ic-difference-positive' : quantity < 0 ? 'ic-difference-negative' : 'ic-difference-zero'}">${quantity > 0 ? '+' : ''}${formatNumber(quantity)}</span></td>
                    <td class="ic-number-column">${formatMoney(item.valueImpact)}</td>
                    <td>${escapeHtml(reasonLabel(item.reasonCode))}<span class="ic-product-meta">${escapeHtml(item.referenceType || '')}${item.referenceId ? ` #${item.referenceId}` : ''}</span></td>
                </tr>`;
        }).join('');
    }

    function showCreateCountModal() {
        byId('countWarehouseId').value = String(state.warehouseId);
        byId('countScopeType').value = state.selectedVariantIds.size > 0 ? 'Cycle' : 'Full';
        byId('countNotes').value = '';
        updateSelectionUi();
        openModal('createCountModal');
    }

    async function createCountSession() {
        const scopeType = byId('countScopeType').value;
        const warehouseId = Number(byId('countWarehouseId').value);
        const variantIds = scopeType === 'Cycle' ? Array.from(state.selectedVariantIds) : [];
        if (scopeType === 'Cycle' && variantIds.length === 0) {
            toast('Kiểm kê chu kỳ cần ít nhất một SKU đã chọn.', 'error');
            return;
        }
        const button = byId('createCountButton');
        button.disabled = true;
        try {
            const data = await postJson(`${apiBase}/Sessions/Create`, {
                warehouseId,
                scopeType,
                notes: byId('countNotes').value.trim(),
                variantIds
            });
            closeModal('createCountModal');
            toast(data.message || 'Đã tạo phiên kiểm kê.', 'success');
            state.selectedVariantIds.clear();
            updateSelectionUi();
            await loadSessions();
            await loadStock();
            switchTab('counts');
            await openSession(Number(data.countSessionId));
        } catch (error) {
            toast(error.message, 'error');
        } finally {
            button.disabled = false;
        }
    }

    async function openSession(sessionId) {
        try {
            const data = await getJson(`${apiBase}/Sessions/${sessionId}`);
            state.currentSession = data;
            state.dirtyLineIds.clear();
            renderSessionDetail();
            openModal('sessionDetailModal');
        } catch (error) {
            toast(error.message, 'error');
        }
    }

    function reasonOptions(selectedCode) {
        const options = ['<option value="">-- Chọn lý do --</option>'];
        for (const item of state.bootstrap?.reasons || []) {
            options.push(`<option value="${escapeHtml(item.code)}" ${item.code === selectedCode ? 'selected' : ''}>${escapeHtml(item.label)}</option>`);
        }
        return options.join('');
    }

    function renderSessionDetail() {
        const data = state.currentSession;
        if (!data) return;
        const session = data.session;
        const editable = session.status === 'Counting';
        const status = statusPresentation(session.status);
        byId('sessionWarehouseLabel').textContent = `${session.warehouseCode} · ${session.warehouseName}`;
        byId('sessionDetailTitle').textContent = session.countCode;
        byId('sessionDetailMeta').innerHTML = `${session.scopeType === 'Full' ? 'Kiểm kê toàn kho' : 'Kiểm kê chu kỳ'} · <span class="ic-badge ${status.className}">${status.label}</span> · Tạo ${escapeHtml(formatDateTime(session.createdAt))}`;

        const lines = data.lines || [];
        byId('countLineTableBody').innerHTML = lines.map(line => {
            const counted = line.countedQuantity ?? '';
            const difference = line.countedQuantity == null ? null : Number(line.countedQuantity) - Number(line.systemQuantity);
            const diffClass = difference > 0 ? 'ic-difference-positive' : difference < 0 ? 'ic-difference-negative' : 'ic-difference-zero';
            const rowClass = difference !== null && difference !== 0 ? 'has-difference' : '';
            const cost = line.adjustmentUnitCost ?? line.unitCostSnapshot ?? 0;
            return `
                <tr class="ic-count-row ${rowClass}" data-line-id="${line.countLineId}" data-system-quantity="${line.systemQuantity}">
                    <td>
                        <div class="ic-product-cell">
                            <img src="${escapeHtml(line.imageUrl)}" alt="" onerror="this.src='/images/products/default-product.png'" />
                            <div><span class="ic-product-name">${escapeHtml(line.productName)}</span><span class="ic-product-meta"><span class="ic-sku">${escapeHtml(line.sku)}</span> · ${escapeHtml(line.variantLabel)}</span></div>
                        </div>
                    </td>
                    <td class="ic-number-column"><strong>${formatNumber(line.systemQuantity)}</strong></td>
                    <td class="ic-number-column"><input class="ic-count-input" type="number" min="0" step="1" value="${counted}" ${editable ? '' : 'disabled'} /></td>
                    <td class="ic-number-column"><span class="ic-line-difference ${diffClass}">${difference === null ? '—' : `${difference > 0 ? '+' : ''}${formatNumber(difference)}`}</span></td>
                    <td><select class="ic-count-select" ${editable ? '' : 'disabled'}>${reasonOptions(line.reasonCode)}</select></td>
                    <td class="ic-number-column"><input class="ic-count-cost" type="number" min="0" step="100" value="${Number(cost || 0)}" ${editable ? '' : 'disabled'} /></td>
                    <td><input class="ic-count-note" type="text" maxlength="500" value="${escapeHtml(line.note || '')}" ${editable ? '' : 'disabled'} placeholder="Ghi chú dòng" /></td>
                </tr>`;
        }).join('');

        if (lines.length === 0) {
            byId('countLineTableBody').innerHTML = '<tr class="ic-empty-row"><td colspan="7">Phiên không có dòng kiểm kê.</td></tr>';
        }

        all('.ic-count-row input, .ic-count-row select').forEach(input => {
            input.addEventListener('input', markLineDirty);
            input.addEventListener('change', markLineDirty);
        });
        updateSessionSummary();
        updateSessionActions();
        setSessionAlert(session);
    }

    function setSessionAlert(session) {
        const alert = byId('sessionAlert');
        alert.className = 'ic-session-alert';
        if (session.status === 'Counting') {
            alert.classList.add('is-warning');
            alert.textContent = 'Nhập số thực đếm. Dòng có chênh lệch phải chọn lý do; hàng tăng thêm cần giá vốn dương.';
        } else if (session.status === 'PendingApproval') {
            alert.classList.add('is-warning');
            alert.textContent = 'Phiên đang chờ duyệt. Khi ghi sổ, hệ thống sẽ kiểm tra tồn hiện tại còn khớp snapshot hay không.';
        } else if (session.status === 'Posted') {
            alert.classList.add('is-success');
            alert.textContent = `Phiên đã ghi sổ lúc ${formatDateTime(session.postedAt)}. Dữ liệu chỉ đọc; muốn điều chỉnh thêm phải lập phiên mới.`;
        } else if (session.status === 'Cancelled') {
            alert.classList.add('is-danger');
            alert.textContent = 'Phiên đã hủy và không làm thay đổi tồn kho.';
        }
    }

    function markLineDirty(event) {
        const row = event.currentTarget.closest('.ic-count-row');
        if (!row) return;
        state.dirtyLineIds.add(Number(row.dataset.lineId));
        row.classList.add('is-dirty');
        updateLineDifference(row);
        updateSessionSummary();
    }

    function updateLineDifference(row) {
        const systemQuantity = Number(row.dataset.systemQuantity || 0);
        const countedInput = row.querySelector('.ic-count-input');
        const raw = countedInput.value;
        const differenceLabel = row.querySelector('.ic-line-difference');
        if (raw === '') {
            differenceLabel.textContent = '—';
            differenceLabel.className = 'ic-line-difference ic-difference-zero';
            row.classList.remove('has-difference');
            return;
        }
        const difference = Number(raw) - systemQuantity;
        differenceLabel.textContent = `${difference > 0 ? '+' : ''}${formatNumber(difference)}`;
        differenceLabel.className = `ic-line-difference ${difference > 0 ? 'ic-difference-positive' : difference < 0 ? 'ic-difference-negative' : 'ic-difference-zero'}`;
        row.classList.toggle('has-difference', difference !== 0);
    }

    function updateSessionSummary() {
        const rows = all('.ic-count-row');
        let counted = 0;
        let discrepancies = 0;
        for (const row of rows) {
            const raw = row.querySelector('.ic-count-input')?.value;
            if (raw === undefined || raw === '') continue;
            counted += 1;
            if (Number(raw) !== Number(row.dataset.systemQuantity || 0)) discrepancies += 1;
        }
        byId('sessionTotal').textContent = `${formatNumber(counted)} / ${formatNumber(rows.length)} dòng đã đếm · ${formatNumber(discrepancies)} dòng chênh lệch · ${formatNumber(state.dirtyLineIds.size)} dòng chưa lưu`;
    }

    function updateSessionActions() {
        const status = state.currentSession?.session?.status;
        byId('saveLinesButton').hidden = status !== 'Counting';
        byId('submitSessionButton').hidden = status !== 'Counting';
        byId('postSessionButton').hidden = status !== 'PendingApproval';
        byId('cancelSessionButton').hidden = status === 'Posted' || status === 'Cancelled';
    }

    function linePayload(row) {
        const countedRaw = row.querySelector('.ic-count-input').value;
        if (countedRaw === '') throw new Error('Có dòng chưa nhập số thực đếm.');
        const countedQuantity = Number(countedRaw);
        if (!Number.isInteger(countedQuantity) || countedQuantity < 0) throw new Error('Số thực đếm phải là số nguyên không âm.');
        const systemQuantity = Number(row.dataset.systemQuantity || 0);
        const reasonCode = row.querySelector('.ic-count-select').value;
        if (countedQuantity !== systemQuantity && !reasonCode) throw new Error('Dòng có chênh lệch phải chọn lý do.');
        return {
            countedQuantity,
            reasonCode: reasonCode || null,
            adjustmentUnitCost: Number(row.querySelector('.ic-count-cost').value || 0),
            note: row.querySelector('.ic-count-note').value.trim()
        };
    }

    async function saveDirtyLines({ silent = false } = {}) {
        if (!state.currentSession || state.dirtyLineIds.size === 0) return true;
        const sessionId = Number(state.currentSession.session.countSessionId);
        const lineIds = Array.from(state.dirtyLineIds);
        const button = byId('saveLinesButton');
        button.disabled = true;
        try {
            for (const lineId of lineIds) {
                const row = document.querySelector(`.ic-count-row[data-line-id="${lineId}"]`);
                if (!row) continue;
                const payload = linePayload(row);
                await postJson(`${apiBase}/Sessions/${sessionId}/Lines/${lineId}/Count`, payload);
                state.dirtyLineIds.delete(lineId);
                row.classList.remove('is-dirty');
            }
            if (!silent) toast('Đã lưu các dòng kiểm đếm.', 'success');
            await refreshCurrentSession(false);
            return true;
        } catch (error) {
            toast(error.message, 'error');
            return false;
        } finally {
            button.disabled = false;
            updateSessionSummary();
        }
    }

    async function refreshCurrentSession(reopen = true) {
        const sessionId = state.currentSession?.session?.countSessionId;
        if (!sessionId) return;
        const data = await getJson(`${apiBase}/Sessions/${sessionId}`);
        state.currentSession = data;
        state.dirtyLineIds.clear();
        renderSessionDetail();
        if (reopen) openModal('sessionDetailModal');
    }

    async function submitCurrentSession() {
        if (!(await saveDirtyLines({ silent: true }))) return;
        const sessionId = state.currentSession?.session?.countSessionId;
        if (!sessionId) return;
        const button = byId('submitSessionButton');
        button.disabled = true;
        try {
            const data = await postJson(`${apiBase}/Sessions/${sessionId}/Submit`, {});
            toast(data.message, 'success');
            await Promise.all([refreshCurrentSession(false), loadSessions(), loadStock()]);
        } catch (error) {
            toast(error.message, 'error');
        } finally {
            button.disabled = false;
        }
    }

    async function postCurrentSession() {
        const sessionId = state.currentSession?.session?.countSessionId;
        if (!sessionId) return;
        if (!window.confirm('Ghi sổ sẽ thay đổi tồn kho và giá trị tồn. Tiếp tục?')) return;
        const button = byId('postSessionButton');
        button.disabled = true;
        try {
            const data = await postJson(`${apiBase}/Sessions/${sessionId}/Post`, {});
            toast(`${data.message} Giá trị chênh lệch: ${formatMoney(data.varianceValue)}.`, 'success');
            await Promise.all([refreshCurrentSession(false), loadSessions(), loadStock(), loadTransactions()]);
        } catch (error) {
            const stale = error.payload?.staleItems;
            if (Array.isArray(stale) && stale.length > 0) {
                byId('sessionAlert').className = 'ic-session-alert is-danger';
                byId('sessionAlert').textContent = `${error.message} SKU lệch snapshot: ${stale.map(item => `#${item.variantId} (${item.snapshotQuantity}→${item.currentQuantity})`).join(', ')}`;
            }
            toast(error.message, 'error');
        } finally {
            button.disabled = false;
        }
    }

    async function cancelCurrentSession() {
        const sessionId = state.currentSession?.session?.countSessionId;
        if (!sessionId) return;
        const reason = window.prompt('Lý do hủy phiên kiểm kê:');
        if (reason === null) return;
        const button = byId('cancelSessionButton');
        button.disabled = true;
        try {
            const data = await postJson(`${apiBase}/Sessions/${sessionId}/Cancel`, { reason });
            toast(data.message, 'success');
            await Promise.all([refreshCurrentSession(false), loadSessions(), loadStock()]);
        } catch (error) {
            toast(error.message, 'error');
        } finally {
            button.disabled = false;
        }
    }

    function switchTab(tabName) {
        all('.ic-tab').forEach(tab => tab.classList.toggle('is-active', tab.dataset.tab === tabName));
        all('[data-panel]').forEach(panel => { panel.hidden = panel.dataset.panel !== tabName; });
        if (tabName === 'counts') loadSessions();
        if (tabName === 'ledger') loadTransactions();
    }

    function bindEvents() {
        byId('warehouseFilter').addEventListener('change', async event => {
            state.warehouseId = Number(event.target.value);
            state.page = 1;
            state.selectedVariantIds.clear();
            updateWarehouseMeta();
            updateSelectionUi();
            await Promise.all([loadStock(), loadSessions(), loadTransactions()]);
        });

        let searchTimer;
        byId('stockSearch').addEventListener('input', () => {
            window.clearTimeout(searchTimer);
            searchTimer = window.setTimeout(() => { state.page = 1; loadStock(); }, 320);
        });
        byId('stockScopeFilter').addEventListener('change', () => { state.page = 1; loadStock(); });
        byId('refreshButton').addEventListener('click', () => Promise.all([loadStock(), loadSessions(), loadTransactions()]));
        byId('previousPageButton').addEventListener('click', () => { if (state.page > 1) { state.page -= 1; loadStock(); } });
        byId('nextPageButton').addEventListener('click', () => { if (state.page < state.totalPages) { state.page += 1; loadStock(); } });
        byId('clearSelectionButton').addEventListener('click', () => { state.selectedVariantIds.clear(); document.querySelectorAll('[data-stock-checkbox]').forEach((checkbox) => { checkbox.checked = false; }); updateSelectionUi(); });
        byId('selectPageCheckbox').addEventListener('change', event => {
            for (const item of state.stockItems) {
                if (event.target.checked) state.selectedVariantIds.add(Number(item.variantId));
                else state.selectedVariantIds.delete(Number(item.variantId));
            }
            all('.stock-row-checkbox').forEach(checkbox => { checkbox.checked = event.target.checked; });
            updateSelectionUi();
        });
        byId('openCountModalButton').addEventListener('click', showCreateCountModal);
        byId('createCountButton').addEventListener('click', createCountSession);
        byId('sessionStatusFilter').addEventListener('change', loadSessions);
        byId('saveLinesButton').addEventListener('click', () => saveDirtyLines());
        byId('submitSessionButton').addEventListener('click', submitCurrentSession);
        byId('postSessionButton').addEventListener('click', postCurrentSession);
        byId('cancelSessionButton').addEventListener('click', cancelCurrentSession);
        all('.ic-tab').forEach(tab => tab.addEventListener('click', () => switchTab(tab.dataset.tab)));
        all('[data-close-modal]').forEach(button => button.addEventListener('click', () => closeModal(button.dataset.closeModal)));
        all('.ic-modal').forEach(modal => modal.addEventListener('click', event => {
            if (event.target === modal) closeModal(modal.id);
        }));
        document.addEventListener('keydown', event => {
            if (event.key !== 'Escape') return;
            const open = document.querySelector('.ic-modal:not([hidden])');
            if (open) closeModal(open.id);
        });
    }

    async function initialize() {
        bindEvents();
        try {
            await loadBootstrap();
            await Promise.all([loadStock(), loadSessions(), loadTransactions()]);
        } catch (error) {
            toast(error.message || 'Không khởi tạo được module tồn kho.', 'error');
            renderStockError(error);
        }
    }

    document.addEventListener('DOMContentLoaded', initialize);
})();
