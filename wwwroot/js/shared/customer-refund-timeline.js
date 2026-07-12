(() => {
    "use strict";

    const MAX_POLLS = 20;
    const POLL_INTERVAL_MS = 15000;

    function escapeHtml(value) {
        return String(value ?? "")
            .replaceAll("&", "&amp;")
            .replaceAll("<", "&lt;")
            .replaceAll(">", "&gt;")
            .replaceAll('"', "&quot;")
            .replaceAll("'", "&#039;");
    }

    function formatMoney(value) {
        const amount = Number(value ?? 0);
        return new Intl.NumberFormat("vi-VN", {
            style: "currency",
            currency: "VND",
            maximumFractionDigits: 0
        }).format(Number.isFinite(amount) ? amount : 0);
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

    function renderEvents(events) {
        if (!Array.isArray(events) || events.length === 0) {
            return "";
        }

        return `
            <div class="customer-refund-card__timeline">
                ${events.map(event => `
                    <div class="customer-refund-card__event customer-refund-card__event--${escapeHtml(event.statusCode)}">
                        <div class="customer-refund-card__event-title">${escapeHtml(event.title)}</div>
                        <div class="customer-refund-card__event-time">${escapeHtml(formatDate(event.occurredAt))}</div>
                        <div class="customer-refund-card__event-description">${escapeHtml(event.description)}</div>
                    </div>
                `).join("")}
            </div>
        `;
    }

    function renderCard(data) {
        const reference = data.transactionReference
            ? `
                <div class="customer-refund-card__fact">
                    <dt>Mã biên lai</dt>
                    <dd>${escapeHtml(data.transactionReference)}</dd>
                </div>
            `
            : "";

        return `
            <section class="customer-refund-card" aria-label="Tiến trình hoàn tiền">
                <header class="customer-refund-card__header">
                    <div>
                        <div class="customer-refund-card__eyebrow">${escapeHtml(data.refundType)}</div>
                        <h3 class="customer-refund-card__title">Tiến trình hoàn tiền</h3>
                    </div>
                    <span class="customer-refund-card__badge customer-refund-card__badge--${escapeHtml(data.statusCode)}">
                        ${escapeHtml(data.statusText)}
                    </span>
                </header>

                <div class="customer-refund-card__body">
                    <div class="customer-refund-card__amount">${escapeHtml(formatMoney(data.amount))}</div>
                    <p class="customer-refund-card__message">${escapeHtml(data.message)}</p>

                    <dl class="customer-refund-card__facts">
                        <div class="customer-refund-card__fact">
                            <dt>Phương thức</dt>
                            <dd>${escapeHtml(data.paymentMethod)}</dd>
                        </div>
                        <div class="customer-refund-card__fact">
                            <dt>Nơi nhận tiền</dt>
                            <dd>${escapeHtml(data.destination)}</dd>
                        </div>
                        ${reference}
                    </dl>

                    ${renderEvents(data.events)}

                    <footer class="customer-refund-card__footer">
                        <span class="customer-refund-card__updated">
                            Cập nhật: ${escapeHtml(formatDate(data.updatedAt))}
                        </span>
                        <button type="button"
                                class="customer-refund-card__refresh"
                                data-customer-refund-refresh>
                            Làm mới
                        </button>
                    </footer>
                </div>
            </section>
        `;
    }

    function initialize() {
        const host = document.querySelector(
            "[data-customer-refund-timeline='true']"
        );

        if (!host || host.dataset.initialized === "true") {
            return;
        }

        host.dataset.initialized = "true";

        const orderId = Number(host.dataset.orderId);
        if (!Number.isInteger(orderId) || orderId <= 0) {
            return;
        }

        let pollCount = 0;
        let pollTimer = null;
        let loading = false;

        const mountTarget = document.querySelector(".summary-sticky-box");

        async function loadTimeline(manualRefresh = false) {
            if (loading) {
                return;
            }

            loading = true;

            const refreshButton = host.querySelector(
                "[data-customer-refund-refresh]"
            );
            if (refreshButton) {
                refreshButton.disabled = true;
                refreshButton.textContent = "Đang cập nhật...";
            }

            try {
                const response = await fetch(
                    `/api/customer/refunds/timeline/${orderId}`,
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

                if (mountTarget && host.parentElement !== mountTarget) {
                    mountTarget.insertBefore(
                        host,
                        mountTarget.firstChild
                    );
                }

                host.innerHTML = renderCard(data);
                host.hidden = false;

                host.querySelector(
                    "[data-customer-refund-refresh]"
                )?.addEventListener(
                    "click",
                    () => loadTimeline(true)
                );

                if (pollTimer) {
                    window.clearTimeout(pollTimer);
                    pollTimer = null;
                }

                if (data.pollingRecommended
                    && pollCount < MAX_POLLS
                    && !manualRefresh) {
                    pollCount += 1;
                    pollTimer = window.setTimeout(
                        () => loadTimeline(false),
                        POLL_INTERVAL_MS
                    );
                }
            } catch {
                if (manualRefresh && host.hidden) {
                    host.hidden = true;
                }
            } finally {
                loading = false;

                const currentRefreshButton = host.querySelector(
                    "[data-customer-refund-refresh]"
                );
                if (currentRefreshButton) {
                    currentRefreshButton.disabled = false;
                    currentRefreshButton.textContent = "Làm mới";
                }
            }
        }

        loadTimeline(false);
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
