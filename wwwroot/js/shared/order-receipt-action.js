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

    function ensureAdminActionsHost() {
        const sidebar = findAdminSidebar();
        if (!sidebar) {
            return null;
        }

        ensureAdminStylesheet();

        let host = sidebar.querySelector(
            ":scope > [data-order-tools-card] [data-order-actions]"
        );

        if (host) {
            return host;
        }

        const card = document.createElement("section");
        card.className = "admin-card order-detail-tools-card";
        card.dataset.orderToolsCard = "true";
        card.innerHTML = `
            <h3 class="order-detail-tools-card__title">
                Công cụ đơn hàng
            </h3>
            <div class="order-detail-tools-card__actions"
                 data-order-actions="true"></div>`;

        const summaryHost = sidebar.querySelector(
            ":scope > [data-order-summary-host]"
        );
        const insertBefore = summaryHost?.nextElementSibling
            ?? (sidebar.children.length > 1
                ? sidebar.children[1]
                : null);

        sidebar.insertBefore(card, insertBefore);
        host = card.querySelector("[data-order-actions]");
        return host;
    }

    function findTarget(mode) {
        if (mode === "order-placed") {
            return document.querySelector(".action-row");
        }

        if (mode === "customer") {
            return document.querySelector(
                ".summary-sticky-box"
            );
        }

        if (mode === "admin") {
            return ensureAdminActionsHost();
        }

        return null;
    }

    function initialize() {
        const actions = document.querySelectorAll(
            "[data-order-receipt-action='true']"
        );

        actions.forEach(action => {
            if (action.dataset.mounted === "true") {
                return;
            }

            action.dataset.mounted = "true";

            const mode = action.dataset.mode ?? "customer";
            const target = findTarget(mode);

            if (target
                && target !== action
                && !action.contains(target)) {
                if (mode === "admin") {
                    action.classList.add(
                        "order-receipt-action--inline"
                    );
                }

                target.appendChild(action);
                action.hidden = false;
                return;
            }

            if (mode === "admin") {
                action.hidden = true;
                console.warn(
                    "Không tìm thấy khu vực công cụ của chi tiết đơn hàng."
                );
                return;
            }

            action.classList.add(
                "order-receipt-action--floating"
            );
            action.hidden = false;
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
