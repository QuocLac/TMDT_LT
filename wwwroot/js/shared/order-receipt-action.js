(() => {
    "use strict";

    function findTarget(mode) {
        if (mode === "order-placed") {
            return document.querySelector(".action-row");
        }

        if (mode === "customer") {
            return document.querySelector(
                ".summary-sticky-box"
            );
        }

        const adminSelectors = [
            "[data-order-actions]",
            ".order-actions",
            ".detail-actions",
            ".order-detail-sidebar",
            ".content-wrapper main",
            ".main-content",
            "main"
        ];

        for (const selector of adminSelectors) {
            const target =
                document.querySelector(selector);

            if (target) {
                return target;
            }
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

            const mode =
                action.dataset.mode ?? "customer";
            const target = findTarget(mode);

            if (target
                && target !== action
                && !action.contains(target)) {
                target.appendChild(action);
                action.hidden = false;
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
