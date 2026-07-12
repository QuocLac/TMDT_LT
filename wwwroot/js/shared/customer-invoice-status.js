(() => {
    "use strict";

    const POLL_INTERVAL_MS = 15000;
    const MAX_POLLS = 20;

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function formatDate(value) {
        if (!value) {
            return "Chưa cập nhật";
        }

        const date = new Date(value);
        if (Number.isNaN(date.getTime())) {
            return "Chưa cập nhật";
        }

        return new Intl.DateTimeFormat("vi-VN", {
            day: "2-digit",
            month: "2-digit",
            year: "numeric",
            hour: "2-digit",
            minute: "2-digit"
        }).format(date);
    }

    function renderFact(label, value) {
        if (!value) {
            return "";
        }

        return `
            <div class="customer-invoice-status__fact">
                <dt>${escapeHtml(label)}</dt>
                <dd>${escapeHtml(value)}</dd>
            </div>
        `;
    }

    function renderActions(data) {
        if (data.statusCode !== "issued") {
            return "";
        }

        const lookupLink = data.invoiceLookupUrl
            ? `
                <a class="customer-invoice-status__button customer-invoice-status__button--primary"
                   href="${escapeHtml(data.invoiceLookupUrl)}"
                   target="_blank"
                   rel="noopener noreferrer">
                    Mở trang tra cứu
                </a>
            `
            : "";

        const copyButton = data.invoiceLookupCode
            ? `
                <button type="button"
                        class="customer-invoice-status__button"
                        data-copy-invoice-code="${escapeHtml(data.invoiceLookupCode)}">
                    Sao chép mã tra cứu
                </button>
            `
            : "";

        if (!lookupLink && !copyButton) {
            return "";
        }

        return `
            <div class="customer-invoice-status__actions">
                ${lookupLink}
                ${copyButton}
            </div>
        `;
    }

    function renderCard(data) {
        const rejectionReason = data.rejectionReason
            ? `
                <div class="customer-invoice-status__reason">
                    <strong>Phản hồi:</strong>
                    ${escapeHtml(data.rejectionReason)}
                </div>
            `
            : "";

        return `
            <section class="customer-invoice-status"
                     aria-label="Trạng thái yêu cầu hóa đơn">
                <header class="customer-invoice-status__header">
                    <div>
                        <div class="customer-invoice-status__eyebrow">
                            Đơn #${escapeHtml(data.orderId)}
                        </div>
                        <h3 class="customer-invoice-status__title">
                            Hóa đơn điện tử
                        </h3>
                    </div>
                    <span class="customer-invoice-status__badge customer-invoice-status__badge--${escapeHtml(data.statusCode)}">
                        ${escapeHtml(data.statusText)}
                    </span>
                </header>

                <div class="customer-invoice-status__body">
                    <p class="customer-invoice-status__message">
                        ${escapeHtml(data.message)}
                    </p>

                    <dl class="customer-invoice-status__facts">
                        ${renderFact("Loại người mua", data.buyerType)}
                        ${renderFact("Tên người mua / đơn vị", data.buyerName)}
                        ${renderFact("Mã số thuế", data.taxCode)}
                        ${renderFact("Email nhận hóa đơn", data.buyerEmail)}
                        ${renderFact("Số điện thoại", data.buyerPhone)}
                        ${renderFact("Địa chỉ hóa đơn", data.buyerAddress)}
                        ${renderFact("Số / ký hiệu hóa đơn", data.invoiceNumber)}
                        ${renderFact("Mã tra cứu", data.invoiceLookupCode)}
                    </dl>

                    ${rejectionReason}
                    ${renderActions(data)}

                    <footer class="customer-invoice-status__footer">
                        <span class="customer-invoice-status__updated">
                            Cập nhật: ${escapeHtml(formatDate(data.updatedAt))}
                        </span>
                        <button type="button"
                                class="customer-invoice-status__refresh"
                                data-invoice-status-refresh>
                            Làm mới
                        </button>
                    </footer>
                </div>
            </section>
        `;
    }

    async function copyCode(value, button) {
        try {
            await navigator.clipboard.writeText(value);
            button.textContent = "Đã sao chép";
        } catch {
            const input = document.createElement("textarea");
            input.value = value;
            input.style.position = "fixed";
            input.style.opacity = "0";
            document.body.appendChild(input);
            input.select();
            document.execCommand("copy");
            input.remove();
            button.textContent = "Đã sao chép";
        }

        window.setTimeout(() => {
            button.textContent = "Sao chép mã tra cứu";
        }, 1800);
    }

    function findTarget(mode) {
        if (mode === "order-placed") {
            return document.querySelector(".success-card");
        }

        return document.querySelector(
            ".summary-sticky-box"
        );
    }

    function initialize() {
        const host = document.querySelector(
            "[data-customer-invoice-status='true']"
        );

        if (!host
            || host.dataset.initialized === "true") {
            return;
        }

        host.dataset.initialized = "true";

        const orderId = Number(host.dataset.orderId);
        if (!Number.isInteger(orderId)
            || orderId <= 0) {
            return;
        }

        const target = findTarget(
            host.dataset.mode ?? "customer"
        );

        let loading = false;
        let pollCount = 0;
        let pollTimer = null;

        async function loadStatus(manual = false) {
            if (loading) {
                return;
            }

            loading = true;

            const refresh = host.querySelector(
                "[data-invoice-status-refresh]"
            );
            if (refresh) {
                refresh.disabled = true;
                refresh.textContent = "Đang cập nhật...";
            }

            try {
                const response = await fetch(
                    `/api/customer/invoices/status/${orderId}`,
                    {
                        method: "GET",
                        credentials: "same-origin",
                        cache: "no-store",
                        headers: {
                            Accept: "application/json"
                        }
                    }
                );

                if (!response.ok) {
                    return;
                }

                const data = await response.json();

                if (!data.exists) {
                    host.hidden = true;
                    return;
                }

                host.innerHTML = renderCard(data);
                host.hidden = false;

                if (target
                    && host.parentElement !== target) {
                    if (host.dataset.mode
                        === "order-placed") {
                        const actionRow =
                            target.querySelector(".action-row");

                        if (actionRow) {
                            target.insertBefore(
                                host,
                                actionRow
                            );
                        } else {
                            target.appendChild(host);
                        }
                    } else {
                        target.insertBefore(
                            host,
                            target.firstChild
                        );
                    }
                } else if (!target) {
                    host.classList.add(
                        "customer-invoice-status--floating"
                    );
                }

                host.querySelector(
                    "[data-invoice-status-refresh]"
                )?.addEventListener(
                    "click",
                    () => loadStatus(true)
                );

                host.querySelectorAll(
                    "[data-copy-invoice-code]"
                ).forEach(button => {
                    button.addEventListener(
                        "click",
                        () => copyCode(
                            button.dataset
                                .copyInvoiceCode ?? "",
                            button
                        )
                    );
                });

                if (pollTimer) {
                    window.clearTimeout(pollTimer);
                    pollTimer = null;
                }

                if (data.pollingRecommended
                    && pollCount < MAX_POLLS
                    && !manual) {
                    pollCount += 1;
                    pollTimer = window.setTimeout(
                        () => loadStatus(false),
                        POLL_INTERVAL_MS
                    );
                }
            } catch {
                // Trang đơn hàng vẫn hoạt động nếu API tạm thời lỗi.
            } finally {
                loading = false;

                const currentRefresh =
                    host.querySelector(
                        "[data-invoice-status-refresh]"
                    );
                if (currentRefresh) {
                    currentRefresh.disabled = false;
                    currentRefresh.textContent = "Làm mới";
                }
            }
        }

        loadStatus(false);
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
