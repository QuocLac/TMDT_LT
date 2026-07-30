(() => {
    "use strict";

    const ADMIN_CONTENT_SELECTOR =
        ".admin-main-content > .content-wrapper";

    function ensureAdminStylesheet() {
        if (document.querySelector(
            "link[data-order-details-integrations-css]"
        )) {
            return;
        }

        const link = document.createElement("link");
        link.rel = "stylesheet";
        link.href =
            "/css/admin/order-details-integrations.css?v=1.0.0";
        link.dataset.orderDetailsIntegrationsCss = "true";
        document.head.appendChild(link);
    }

    function findAdminSidebar() {
        const content = document.querySelector(
            ADMIN_CONTENT_SELECTOR
        );

        if (!content) {
            return null;
        }

        for (const layout of Array.from(content.children)) {
            if (!(layout instanceof HTMLElement)
                || layout.children.length < 2) {
                continue;
            }

            const layoutStyle = window.getComputedStyle(layout);
            if (layoutStyle.display !== "grid") {
                continue;
            }

            const sidebar = layout.lastElementChild;
            if (!(sidebar instanceof HTMLElement)) {
                continue;
            }

            const sidebarStyle = window.getComputedStyle(sidebar);
            if (sidebarStyle.display !== "flex"
                || sidebarStyle.flexDirection !== "column") {
                continue;
            }

            layout.classList.add("order-detail-layout");
            sidebar.classList.add("order-detail-sidebar");
            return sidebar;
        }

        return null;
    }

    function ensureAdminSummaryHost() {
        const sidebar = findAdminSidebar();
        if (!sidebar) {
            return null;
        }

        ensureAdminStylesheet();

        let host = sidebar.querySelector(
            ":scope > [data-order-summary-host]"
        );

        if (!host) {
            host = document.createElement("div");
            host.className = "order-detail-summary-host";
            host.dataset.orderSummaryHost = "true";

            const insertBefore = sidebar.children.length > 1
                ? sidebar.children[1]
                : null;

            sidebar.insertBefore(host, insertBefore);
        }

        return host;
    }

    function findTarget(mode) {
        if (mode === "customer") {
            return document.querySelector(
                ".summary-sticky-box"
            );
        }

        if (mode === "admin") {
            return ensureAdminSummaryHost();
        }

        return null;
    }

    function initialize() {
        const cards = document.querySelectorAll(
            "[data-order-financial-summary='true']"
        );

        cards.forEach(card => {
            if (card.dataset.mounted === "true") {
                return;
            }

            card.dataset.mounted = "true";

            const mode = card.dataset.mode ?? "customer";
            const target = findTarget(mode);

            if (target
                && target !== card
                && !card.contains(target)) {
                target.appendChild(card);
                card.hidden = false;
                return;
            }

            if (mode === "admin") {
                card.hidden = true;
                console.warn(
                    "Không tìm thấy sidebar chi tiết đơn hàng để gắn bảng thanh toán."
                );
                return;
            }

            card.classList.add(
                "order-financial-summary--floating"
            );
            card.hidden = false;
        });
    }

    if (document.readyState === "loading") {
        document.addEventListener(
            "DOMContentLoaded",
            initialize,
            { once: true }
        );
    } else {
        initialize();
    }
})();
