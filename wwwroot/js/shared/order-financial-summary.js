(() => {
    "use strict";

    function findTarget(mode) {
        if (mode === "customer") {
            return document.querySelector(
                ".summary-sticky-box"
            );
        }

        const adminSelectors = [
            "[data-order-summary-host]",
            ".order-summary-card",
            ".order-detail-sidebar",
            ".detail-sidebar",
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
        const cards = document.querySelectorAll(
            "[data-order-financial-summary='true']"
        );

        cards.forEach(card => {
            if (card.dataset.mounted === "true") {
                return;
            }

            card.dataset.mounted = "true";

            const mode =
                card.dataset.mode ?? "customer";
            const target = findTarget(mode);

            if (target
                && !card.contains(target)
                && target !== card) {
                target.insertBefore(
                    card,
                    target.firstChild
                );
                return;
            }

            card.classList.add(
                "order-financial-summary--floating"
            );
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
