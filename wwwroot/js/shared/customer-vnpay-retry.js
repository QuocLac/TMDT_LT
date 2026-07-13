(() => {
    "use strict";

    const card = document.querySelector(
        "[data-vnpay-retry-card]"
    );

    if (!card) {
        return;
    }

    const summary = document.querySelector(
        ".summary-sticky-box"
    );

    if (!summary) {
        return;
    }

    const paymentCards = summary.querySelectorAll(
        ".checkout-section-box"
    );

    if (paymentCards.length >= 2) {
        paymentCards[1].insertAdjacentElement(
            "afterend",
            card
        );
    } else {
        summary.prepend(card);
    }

    card.hidden = false;

    const form = card.querySelector(
        "[data-vnpay-retry-form]"
    );
    const button = card.querySelector(
        "[data-vnpay-retry-button]"
    );
    const countdown = card.querySelector(
        "[data-vnpay-countdown]"
    );
    const message = card.querySelector(
        "[data-vnpay-retry-message]"
    );

    const statusUrl = card.dataset.statusUrl || "";
    const awaitingStatus =
        card.dataset.awaitingStatus || "";
    const pendingOrderStatus =
        card.dataset.pendingOrderStatus || "";
    const expiresRaw =
        card.dataset.expiresAt || "";
    const expiresAt = expiresRaw
        ? new Date(expiresRaw)
        : null;

    let pollingStopped = false;

    function showMessage(text) {
        if (!message) {
            return;
        }

        message.textContent = text;
        message.hidden = !text;
    }

    function disableRetry(text) {
        pollingStopped = true;

        if (button) {
            button.disabled = true;
            button.innerHTML =
                '<i class="fas fa-clock"></i> '
                + "Đã hết thời hạn thanh toán";
        }

        if (countdown) {
            countdown.textContent = "Đã hết hạn";
        }

        showMessage(
            text
            || "Hệ thống đang giải phóng phần hàng đã giữ cho đơn này."
        );
    }

    function updateCountdown() {
        if (!expiresAt
            || Number.isNaN(expiresAt.getTime())) {
            return;
        }

        const remaining =
            expiresAt.getTime() - Date.now();

        if (remaining <= 0) {
            disableRetry();
            return;
        }

        if (!countdown) {
            return;
        }

        const totalSeconds =
            Math.floor(remaining / 1000);
        const minutes =
            Math.floor(totalSeconds / 60);
        const seconds =
            totalSeconds % 60;

        countdown.textContent =
            `Còn ${minutes}:${String(seconds)
                .padStart(2, "0")}`;
    }

    async function pollStatus() {
        if (pollingStopped || !statusUrl) {
            return;
        }

        try {
            const response = await fetch(
                statusUrl,
                {
                    credentials: "same-origin",
                    headers: {
                        Accept: "application/json"
                    },
                    cache: "no-store"
                }
            );

            if (!response.ok) {
                return;
            }

            const data = await response.json();

            if (data.paymentStatus
                    !== awaitingStatus
                || data.orderStatus
                    !== pendingOrderStatus) {
                pollingStopped = true;
                window.location.reload();
            }
        } catch {
            // Không làm hỏng trang nếu polling tạm thời thất bại.
        }
    }

    if (form && button) {
        form.addEventListener(
            "submit",
            () => {
                button.disabled = true;
                button.innerHTML =
                    '<i class="fas fa-spinner fa-spin"></i> '
                    + "Đang mở cổng VNPAY...";
                showMessage(
                    "Vui lòng không gửi lại nhiều lần."
                );
            }
        );
    }

    updateCountdown();

    if (!pollingStopped) {
        window.setInterval(
            updateCountdown,
            1000
        );
        window.setInterval(
            pollStatus,
            5000
        );
    }
})();
