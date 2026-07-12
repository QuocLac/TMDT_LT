(() => {
    "use strict";

    function findPaymentSection() {
        const sections = document.querySelectorAll(
            ".checkout-section-box"
        );

        for (const section of sections) {
            const title = section.querySelector(
                ".section-title"
            );

            if (title?.textContent
                ?.toLowerCase()
                .includes("phương thức thanh toán")) {
                return section;
            }
        }

        return null;
    }

    function initialize() {
        const section = document.querySelector(
            "[data-checkout-invoice-request='true']"
        );

        if (!section
            || section.dataset.initialized === "true") {
            return;
        }

        section.dataset.initialized = "true";

        const paymentSection = findPaymentSection();
        if (paymentSection?.parentElement) {
            paymentSection.parentElement.insertBefore(
                section,
                paymentSection
            );
        }

        const toggle = section.querySelector(
            "[data-invoice-toggle]"
        );
        const fields = section.querySelector(
            "[data-invoice-fields]"
        );
        const inputs = Array.from(
            section.querySelectorAll(
                "[data-invoice-input]"
            )
        );
        const buyerType = section.querySelector(
            "[data-invoice-buyer-type]"
        );
        const taxCode = section.querySelector(
            "[data-tax-code-input]"
        );
        const taxCodeRequired = section.querySelector(
            "[data-tax-code-required]"
        );

        function syncBuyerType() {
            const organization =
                buyerType?.value === "Organization";

            if (taxCode) {
                taxCode.required = organization;
            }

            if (taxCodeRequired) {
                taxCodeRequired.textContent = organization
                    ? "Bắt buộc với tổ chức"
                    : "Không bắt buộc với cá nhân";
            }
        }

        function syncEnabledState() {
            const enabled = toggle?.checked === true;

            if (fields) {
                fields.hidden = !enabled;
            }

            inputs.forEach(input => {
                input.disabled = !enabled;
            });

            if (enabled) {
                syncBuyerType();
            }
        }

        taxCode?.addEventListener("input", () => {
            taxCode.value = taxCode.value
                .replace(/[^0-9-]/g, "")
                .slice(0, 20);
        });

        buyerType?.addEventListener(
            "change",
            syncBuyerType
        );
        toggle?.addEventListener(
            "change",
            syncEnabledState
        );

        syncEnabledState();
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
