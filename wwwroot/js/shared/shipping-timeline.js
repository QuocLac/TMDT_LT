(function () {
    'use strict';

    const host = document.querySelector('[data-shipping-timeline-host="true"]');
    if (!host || host.dataset.initialized === 'true') return;
    host.dataset.initialized = 'true';

    const orderId = Number(host.dataset.orderId || 0);
    const audience = host.dataset.audience || 'customer';
    if (!orderId) return;

    ensureStylesheet();

    host.innerHTML = `
        <button type="button" class="shipping-fab" aria-label="Mở tiến trình vận chuyển">
            <i class="fas fa-truck-fast"></i>
            <span>${audience === 'admin' ? 'Chi tiết vận chuyển' : 'Theo dõi giao hàng'}</span>
            <span class="shipping-fab-badge" hidden></span>
        </button>
        <div class="shipping-drawer-backdrop" hidden></div>
        <aside class="shipping-drawer" aria-hidden="true" aria-label="Tiến trình vận chuyển">
            <div class="shipping-drawer-header">
                <div>
                    <div class="shipping-eyebrow">ĐƠN HÀNG #${orderId}</div>
                    <h2>Tiến trình vận chuyển</h2>
                </div>
                <button type="button" class="shipping-close" aria-label="Đóng">&times;</button>
            </div>
            <div class="shipping-drawer-body">
                <div class="shipping-loading"><span class="shipping-spinner"></span> Đang tải dữ liệu vận chuyển...</div>
            </div>
        </aside>`;

    const openButton = host.querySelector('.shipping-fab');
    const badge = host.querySelector('.shipping-fab-badge');
    const backdrop = host.querySelector('.shipping-drawer-backdrop');
    const drawer = host.querySelector('.shipping-drawer');
    const closeButton = host.querySelector('.shipping-close');
    const body = host.querySelector('.shipping-drawer-body');
    let loaded = false;

    if (audience === 'admin') {
        const actionHost = ensureAdminActionsHost();
        if (!actionHost) {
            openButton.hidden = true;
            console.warn('Không tìm thấy khu vực công cụ để gắn nút vận chuyển.');
        } else {
            openButton.classList.add('shipping-fab--inline');
            actionHost.appendChild(openButton);
        }
    }

    function ensureStylesheet() {
        if (!document.querySelector('link[data-shipping-timeline-css]')) {
            const link = document.createElement('link');
            link.rel = 'stylesheet';
            link.href = '/css/shared/shipping-timeline.css?v=1.0.0';
            link.dataset.shippingTimelineCss = 'true';
            document.head.appendChild(link);
        }

        if (audience === 'admin'
            && !document.querySelector('link[data-order-details-integrations-css]')) {
            const adminLink = document.createElement('link');
            adminLink.rel = 'stylesheet';
            adminLink.href = '/css/admin/order-details-integrations.css?v=1.0.0';
            adminLink.dataset.orderDetailsIntegrationsCss = 'true';
            document.head.appendChild(adminLink);
        }
    }

    function findAdminSidebar() {
        const content = document.querySelector(
            '.admin-main-content > .content-wrapper'
        );

        if (!content) return null;

        for (const layout of Array.from(content.children)) {
            if (!(layout instanceof HTMLElement)
                || layout.children.length < 2) {
                continue;
            }

            const layoutStyle = window.getComputedStyle(layout);
            if (layoutStyle.display !== 'grid') continue;

            const sidebar = layout.lastElementChild;
            if (!(sidebar instanceof HTMLElement)) continue;

            const sidebarStyle = window.getComputedStyle(sidebar);
            if (sidebarStyle.display !== 'flex'
                || sidebarStyle.flexDirection !== 'column') {
                continue;
            }

            layout.classList.add('order-detail-layout');
            sidebar.classList.add('order-detail-sidebar');
            return sidebar;
        }

        return null;
    }

    function ensureAdminActionsHost() {
        const sidebar = findAdminSidebar();
        if (!sidebar) return null;

        let actionHost = sidebar.querySelector(
            ':scope > [data-order-tools-card] [data-order-actions]'
        );
        if (actionHost) return actionHost;

        const card = document.createElement('section');
        card.className = 'admin-card order-detail-tools-card';
        card.dataset.orderToolsCard = 'true';
        card.innerHTML = `
            <h3 class="order-detail-tools-card__title">
                Công cụ đơn hàng
            </h3>
            <div class="order-detail-tools-card__actions"
                 data-order-actions="true"></div>`;

        const summaryHost = sidebar.querySelector(
            ':scope > [data-order-summary-host]'
        );
        const insertBefore = summaryHost?.nextElementSibling
            ?? (sidebar.children.length > 1
                ? sidebar.children[1]
                : null);

        sidebar.insertBefore(card, insertBefore);
        return card.querySelector('[data-order-actions]');
    }

    function openDrawer() {
        backdrop.hidden = false;
        drawer.classList.add('open');
        drawer.setAttribute('aria-hidden', 'false');
        document.documentElement.classList.add('shipping-drawer-open');
        if (!loaded) loadTimeline();
    }

    function closeDrawer() {
        drawer.classList.remove('open');
        drawer.setAttribute('aria-hidden', 'true');
        backdrop.hidden = true;
        document.documentElement.classList.remove('shipping-drawer-open');
    }

    async function loadTimeline(force) {
        body.innerHTML = '<div class="shipping-loading"><span class="shipping-spinner"></span> Đang tải dữ liệu vận chuyển...</div>';

        try {
            const response = await fetch(`/api/shipping/timeline/${encodeURIComponent(orderId)}${force ? '?refresh=1' : ''}`, {
                headers: { 'Accept': 'application/json' },
                credentials: 'same-origin'
            });

            const data = await response.json().catch(() => null);
            if (!response.ok || !data?.success) {
                throw new Error(data?.message || `Không tải được tiến trình vận chuyển (${response.status}).`);
            }

            loaded = true;
            renderTimeline(data);
            badge.textContent = data.currentStatus || '';
            badge.hidden = !data.currentStatus;
        } catch (error) {
            body.innerHTML = `
                <div class="shipping-error-card">
                    <i class="fas fa-circle-exclamation"></i>
                    <strong>Không thể tải tiến trình</strong>
                    <p>${escapeHtml(error?.message || 'Đã xảy ra lỗi không xác định.')}</p>
                    <button type="button" class="shipping-retry">Thử lại</button>
                </div>`;
            body.querySelector('.shipping-retry')?.addEventListener('click', () => loadTimeline(true));
        }
    }

    function renderTimeline(data) {
        const tracking = data.trackingNumber || 'Chưa phát hành';
        const provider = data.provider || 'Chưa chỉ định';
        const eta = formatDate(data.estimatedDelivery);
        const progress = Math.max(0, Math.min(100, Number(data.progress || 0)));
        const events = Array.isArray(data.events) ? data.events : [];

        const adminDetails = data.isAdmin ? `
            <div class="shipping-admin-grid">
                <div><span>Provider status</span><strong>${escapeHtml(data.providerStatus || 'N/A')}</strong></div>
                <div><span>Phí vận chuyển</span><strong>${formatMoney(data.shippingFee)}</strong></div>
                <div><span>COD cần thu</span><strong>${formatMoney(data.codAmount)}</strong></div>
                <div><span>Bảo hiểm</span><strong>${formatMoney(data.insuranceValue)}</strong></div>
                <div><span>Webhook gần nhất</span><strong>${formatDate(data.lastWebhookAt)}</strong></div>
                <div><span>Service type</span><strong>${escapeHtml(data.serviceTypeId ?? 'N/A')}</strong></div>
            </div>
            ${data.lastError ? `<div class="shipping-provider-error"><strong>Lỗi provider:</strong> ${escapeHtml(data.lastError)}</div>` : ''}
        ` : '';

        const eventHtml = events.length
            ? events.map(event => renderEvent(event, data.isAdmin)).join('')
            : '<div class="shipping-empty">Chưa có cập nhật chi tiết từ đơn vị vận chuyển.</div>';

        body.innerHTML = `
            <section class="shipping-summary-card">
                <div class="shipping-status-row">
                    <div>
                        <span class="shipping-status-label">Trạng thái hiện tại</span>
                        <strong class="shipping-current-status">${escapeHtml(data.currentStatus || 'Chưa xác định')}</strong>
                    </div>
                    <button type="button" class="shipping-refresh" title="Làm mới"><i class="fas fa-rotate"></i></button>
                </div>
                <div class="shipping-progress"><span style="width:${progress}%"></span></div>
                <div class="shipping-summary-grid">
                    <div><span>Đơn vị vận chuyển</span><strong>${escapeHtml(provider)}</strong></div>
                    <div><span>Mã vận đơn</span><strong class="shipping-code">${escapeHtml(tracking)}</strong></div>
                    <div><span>Dự kiến giao</span><strong>${eta}</strong></div>
                    <div><span>Trạng thái đơn</span><strong>${escapeHtml(data.orderStatus || 'N/A')}</strong></div>
                </div>
                ${!data.exists ? `<div class="shipping-info-note">${escapeHtml(data.message || 'Đơn chưa có vận đơn.')}</div>` : ''}
                ${adminDetails}
            </section>
            <section class="shipping-events-section">
                <div class="shipping-section-title"><i class="fas fa-route"></i> Các mốc vận chuyển</div>
                <div class="shipping-event-list">${eventHtml}</div>
            </section>`;

        body.querySelector('.shipping-refresh')?.addEventListener('click', () => loadTimeline(true));
    }

    function renderEvent(event, isAdmin) {
        const adminMeta = isAdmin ? `
            <div class="shipping-event-meta">
                ${event.eventType ? `<span>${escapeHtml(event.eventType)}</span>` : ''}
                ${event.providerStatus ? `<span>${escapeHtml(event.providerStatus)}</span>` : ''}
                ${event.processingStatus ? `<span>${escapeHtml(event.processingStatus)}</span>` : ''}
            </div>
            ${event.error ? `<div class="shipping-event-error">${escapeHtml(event.error)}</div>` : ''}
        ` : '';

        return `
            <article class="shipping-event-item">
                <span class="shipping-event-dot"></span>
                <div class="shipping-event-content">
                    <div class="shipping-event-heading">
                        <strong>${escapeHtml(event.title || 'Cập nhật vận chuyển')}</strong>
                        <time>${formatDate(event.occurredAt)}</time>
                    </div>
                    <p>${escapeHtml(event.detail || '')}</p>
                    ${adminMeta}
                </div>
            </article>`;
    }

    function escapeHtml(value) {
        return String(value ?? '')
            .replace(/&/g, '&amp;')
            .replace(/</g, '&lt;')
            .replace(/>/g, '&gt;')
            .replace(/"/g, '&quot;')
            .replace(/'/g, '&#039;');
    }

    function formatDate(value) {
        if (!value) return 'Chưa có';
        const date = new Date(value);
        if (Number.isNaN(date.getTime())) return escapeHtml(value);
        return date.toLocaleString('vi-VN', {
            day: '2-digit', month: '2-digit', year: 'numeric',
            hour: '2-digit', minute: '2-digit'
        });
    }

    function formatMoney(value) {
        if (value === null || value === undefined) return 'Chưa có';
        return Number(value || 0).toLocaleString('vi-VN') + ' đ';
    }

    openButton.addEventListener('click', openDrawer);
    closeButton.addEventListener('click', closeDrawer);
    backdrop.addEventListener('click', closeDrawer);
    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && drawer.classList.contains('open')) closeDrawer();
    });
})();
