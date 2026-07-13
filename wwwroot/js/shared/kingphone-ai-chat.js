(() => {
    'use strict';

    const root = document.getElementById('kingphone-ai-chat');
    if (!root) return;

    const launcher = root.querySelector('.kp-ai-launcher');
    const panel = root.querySelector('.kp-ai-panel');
    const closeButton = root.querySelector('.kp-ai-close');
    const resetButton = root.querySelector('.kp-ai-reset');
    const form = root.querySelector('.kp-ai-form');
    const input = root.querySelector('#kp-ai-input');
    const sendButton = root.querySelector('.kp-ai-send');
    const messagesElement = root.querySelector('.kp-ai-messages');
    const quickRepliesElement = root.querySelector('.kp-ai-quick-replies');
    const endpoint = root.dataset.endpoint || '/AiChat/Send';
    const antiforgery = root.dataset.antiforgery || '';
    const storageKey = 'kingphone_ai_chat_state_v2';
    const maximumMessages = 20;
    const maximumProductsPerMessage = 8;

    const welcomeMessage = 'Chào bạn, mình là Trợ lý KingPhone. Mình có thể kiểm tra trực tiếp sản phẩm, giá, tồn kho và ưu đãi đang áp dụng. Bạn đang ưu tiên ngân sách, hiệu năng, camera, pin hay thiết kế?';
    const state = loadState();
    let isSending = false;

    renderAllMessages();

    launcher?.addEventListener('click', openPanel);
    closeButton?.addEventListener('click', closePanel);
    resetButton?.addEventListener('click', resetConversation);
    form?.addEventListener('submit', submitMessage);
    quickRepliesElement?.addEventListener('click', handleQuickReply);
    messagesElement?.addEventListener('click', handleProductClick);

    input?.addEventListener('keydown', event => {
        if (event.key === 'Enter' && !event.shiftKey) {
            event.preventDefault();
            form?.requestSubmit();
        }
    });

    input?.addEventListener('input', () => {
        input.style.height = 'auto';
        input.style.height = Math.min(input.scrollHeight, 108) + 'px';
    });

    document.addEventListener('keydown', event => {
        if (event.key === 'Escape' && panel && !panel.hidden) {
            closePanel();
        }
    });

    function openPanel() {
        if (!panel) return;
        panel.hidden = false;
        launcher?.setAttribute('aria-expanded', 'true');
        input?.focus();
        scrollToBottom();
        track('ai_chat_open');
    }

    function closePanel() {
        if (!panel) return;
        panel.hidden = true;
        launcher?.setAttribute('aria-expanded', 'false');
        launcher?.focus();
    }

    function resetConversation() {
        if (isSending) return;
        state.messages = [];
        sessionStorage.removeItem(storageKey);
        renderAllMessages();
        renderQuickReplies([
            'Tìm điện thoại còn hàng dưới 15 triệu',
            'So sánh hai sản phẩm',
            'Kiểm tra ưu đãi hiện tại'
        ]);
        input?.focus();
    }

    function handleQuickReply(event) {
        const button = event.target.closest('button[data-message]');
        if (!button || !input || isSending) return;
        input.value = button.dataset.message || button.textContent || '';
        form?.requestSubmit();
    }

    function handleProductClick(event) {
        const link = event.target.closest('.kp-ai-product-card');
        if (!link) return;

        track('ai_product_clicked', {
            productId: parseInteger(link.dataset.productId),
            variantId: parseInteger(link.dataset.variantId),
            metadata: {
                source: 'kingphone_ai_widget'
            }
        });
    }

    async function submitMessage(event) {
        event.preventDefault();
        if (!input || isSending) return;

        const message = input.value.trim();
        if (!message) return;

        const history = state.messages.slice(-8).map(item => ({
            role: item.role,
            content: item.content
        }));

        appendMessage('user', message);
        input.value = '';
        input.style.height = 'auto';
        setSending(true);
        renderQuickReplies([]);
        const loadingElement = appendLoading();
        track('ai_question_sent', { metadata: { source: 'kingphone_ai_widget' } });

        try {
            const response = await fetch(endpoint, {
                method: 'POST',
                credentials: 'same-origin',
                headers: {
                    'Content-Type': 'application/json',
                    'RequestVerificationToken': antiforgery
                },
                body: JSON.stringify({
                    message,
                    pagePath: window.location.pathname + window.location.search,
                    pageTitle: document.title,
                    history
                })
            });

            let data;
            try {
                data = await response.json();
            } catch {
                data = null;
            }

            loadingElement?.remove();

            if (response.status === 429) {
                appendMessage('assistant', 'Bạn đang gửi câu hỏi khá nhanh. Vui lòng chờ một phút rồi tiếp tục để Trợ lý KingPhone phục vụ ổn định hơn.');
                track('ai_chat_error', { metadata: { code: 'RATE_LIMIT' } });
                return;
            }

            if (!response.ok || !data?.success) {
                appendMessage(
                    'assistant',
                    data?.message || 'Trợ lý KingPhone chưa thể phản hồi vào lúc này. Vui lòng thử lại sau ít phút.',
                    normalizeProducts(data?.products)
                );
                renderQuickReplies(data?.quickReplies || []);
                track('ai_chat_error', { metadata: { code: data?.errorCode || 'REQUEST_FAILED' } });
                return;
            }

            const products = normalizeProducts(data.products);
            appendMessage('assistant', data.message, products);
            renderQuickReplies(data.quickReplies || []);
            track('ai_answer_received', {
                metadata: {
                    tools: Array.isArray(data.toolsUsed) ? data.toolsUsed : [],
                    productCount: products.length
                }
            });

            if (products.length > 0) {
                track('ai_product_recommended', {
                    metadata: {
                        count: products.length,
                        productIds: products.map(item => item.productId)
                    }
                });
            }
        } catch (error) {
            loadingElement?.remove();
            appendMessage('assistant', 'Không thể kết nối đến Trợ lý KingPhone. Website và các chức năng mua hàng vẫn hoạt động bình thường.');
            track('ai_chat_error', { metadata: { code: 'NETWORK_ERROR' } });
            console.error('KingPhone AI chat request failed:', error);
        } finally {
            setSending(false);
            input?.focus();
        }
    }

    function appendMessage(role, content, products = []) {
        const normalizedRole = role === 'user' ? 'user' : 'assistant';
        const normalizedProducts = normalizedRole === 'assistant'
            ? normalizeProducts(products)
            : [];

        state.messages.push({
            role: normalizedRole,
            content: String(content || ''),
            products: normalizedProducts
        });
        state.messages = state.messages.slice(-maximumMessages);
        saveState();
        messagesElement?.appendChild(createMessageElement(normalizedRole, content, normalizedProducts));
        scrollToBottom();
    }

    function createMessageElement(role, content, products = []) {
        const wrapper = document.createElement('div');
        wrapper.className = `kp-ai-message kp-ai-message-${role}`;

        if (role === 'assistant') {
            const avatar = document.createElement('div');
            avatar.className = 'kp-ai-avatar';
            avatar.setAttribute('aria-hidden', 'true');
            const icon = document.createElement('i');
            icon.className = 'fa-solid fa-crown';
            avatar.appendChild(icon);
            wrapper.appendChild(avatar);

            const responseColumn = document.createElement('div');
            responseColumn.className = 'kp-ai-response';
            responseColumn.appendChild(createBubble(content));

            const normalizedProducts = normalizeProducts(products);
            if (normalizedProducts.length > 0) {
                responseColumn.appendChild(createProductList(normalizedProducts));
            }

            wrapper.appendChild(responseColumn);
            return wrapper;
        }

        wrapper.appendChild(createBubble(content));
        return wrapper;
    }

    function createBubble(content) {
        const bubble = document.createElement('div');
        bubble.className = 'kp-ai-bubble';
        bubble.textContent = String(content || '');
        return bubble;
    }

    function createProductList(products) {
        const list = document.createElement('div');
        list.className = 'kp-ai-products';
        list.setAttribute('aria-label', 'Sản phẩm KingPhone được gợi ý');

        products.slice(0, maximumProductsPerMessage).forEach(product => {
            list.appendChild(createProductCard(product));
        });

        return list;
    }

    function createProductCard(product) {
        const link = document.createElement('a');
        link.className = 'kp-ai-product-card';
        link.href = safeRelativeUrl(product.url);
        link.dataset.productId = String(product.productId || '');
        link.dataset.variantId = String(product.variantId || '');

        const image = document.createElement('img');
        image.className = 'kp-ai-product-image';
        image.src = safeImageUrl(product.imageUrl);
        image.alt = product.name || 'Sản phẩm KingPhone';
        image.loading = 'lazy';
        image.addEventListener('error', () => {
            image.src = '/images/products/default-product.png';
        }, { once: true });

        const body = document.createElement('div');
        body.className = 'kp-ai-product-body';

        const name = document.createElement('strong');
        name.className = 'kp-ai-product-name';
        name.textContent = product.name || 'Sản phẩm KingPhone';
        body.appendChild(name);

        if (product.variantName) {
            const variant = document.createElement('span');
            variant.className = 'kp-ai-product-variant';
            variant.textContent = product.variantName;
            body.appendChild(variant);
        }

        const priceRow = document.createElement('div');
        priceRow.className = 'kp-ai-product-price-row';

        const price = document.createElement('span');
        price.className = 'kp-ai-product-price';
        price.textContent = formatCurrency(product.price);
        priceRow.appendChild(price);

        if (Number(product.originalPrice || 0) > Number(product.price || 0)) {
            const oldPrice = document.createElement('span');
            oldPrice.className = 'kp-ai-product-old-price';
            oldPrice.textContent = formatCurrency(product.originalPrice);
            priceRow.appendChild(oldPrice);
        }

        body.appendChild(priceRow);

        const statusRow = document.createElement('div');
        statusRow.className = 'kp-ai-product-status-row';

        if (product.isFlashSale) {
            const badge = document.createElement('span');
            badge.className = 'kp-ai-product-badge';
            badge.textContent = 'FLASH SALE';
            statusRow.appendChild(badge);
        }

        const stock = document.createElement('span');
        stock.className = Number(product.stock || 0) > 0
            ? 'kp-ai-product-stock is-available'
            : 'kp-ai-product-stock is-unavailable';
        stock.textContent = product.stockText || (Number(product.stock || 0) > 0 ? 'Còn hàng' : 'Tạm hết hàng');
        statusRow.appendChild(stock);
        body.appendChild(statusRow);

        link.appendChild(image);
        link.appendChild(body);
        return link;
    }

    function appendLoading() {
        if (!messagesElement) return null;
        const wrapper = document.createElement('div');
        wrapper.className = 'kp-ai-message kp-ai-message-assistant kp-ai-message-loading';

        const avatar = document.createElement('div');
        avatar.className = 'kp-ai-avatar';
        avatar.setAttribute('aria-hidden', 'true');
        const icon = document.createElement('i');
        icon.className = 'fa-solid fa-crown';
        avatar.appendChild(icon);

        const bubble = document.createElement('div');
        bubble.className = 'kp-ai-bubble';
        for (let index = 0; index < 3; index += 1) {
            const dot = document.createElement('span');
            dot.className = 'kp-ai-dot';
            bubble.appendChild(dot);
        }

        wrapper.appendChild(avatar);
        wrapper.appendChild(bubble);
        messagesElement.appendChild(wrapper);
        scrollToBottom();
        return wrapper;
    }

    function renderAllMessages() {
        if (!messagesElement) return;
        messagesElement.textContent = '';

        if (state.messages.length === 0) {
            messagesElement.appendChild(createMessageElement('assistant', welcomeMessage));
            return;
        }

        state.messages.forEach(item => {
            messagesElement.appendChild(createMessageElement(item.role, item.content, item.products || []));
        });
        scrollToBottom();
    }

    function renderQuickReplies(items) {
        if (!quickRepliesElement) return;
        quickRepliesElement.textContent = '';

        if (!Array.isArray(items) || items.length === 0) {
            quickRepliesElement.hidden = true;
            return;
        }

        quickRepliesElement.hidden = false;
        items.slice(0, 4).forEach(item => {
            const value = String(item || '').trim();
            if (!value) return;
            const button = document.createElement('button');
            button.type = 'button';
            button.dataset.message = value;
            button.textContent = value;
            quickRepliesElement.appendChild(button);
        });
    }

    function setSending(value) {
        isSending = value;
        if (sendButton) sendButton.disabled = value;
        if (input) input.disabled = value;
    }

    function scrollToBottom() {
        if (!messagesElement) return;
        requestAnimationFrame(() => {
            messagesElement.scrollTop = messagesElement.scrollHeight;
        });
    }

    function normalizeProducts(items) {
        if (!Array.isArray(items)) return [];

        return items
            .filter(item => item && Number(item.productId) > 0 && Number(item.variantId) > 0)
            .slice(0, maximumProductsPerMessage)
            .map(item => ({
                productId: parseInteger(item.productId),
                variantId: parseInteger(item.variantId),
                name: String(item.name || '').slice(0, 180),
                variantName: String(item.variantName || '').slice(0, 160),
                brandName: String(item.brandName || '').slice(0, 100),
                categoryName: String(item.categoryName || '').slice(0, 100),
                price: Number(item.price || 0),
                originalPrice: item.originalPrice == null ? null : Number(item.originalPrice || 0),
                isFlashSale: item.isFlashSale === true,
                stock: Math.max(0, parseInteger(item.stock)),
                stockText: String(item.stockText || '').slice(0, 100),
                imageUrl: String(item.imageUrl || ''),
                url: String(item.url || ''),
                highlights: Array.isArray(item.highlights)
                    ? item.highlights.map(value => String(value || '').slice(0, 120)).slice(0, 4)
                    : []
            }));
    }

    function loadState() {
        try {
            const raw = sessionStorage.getItem(storageKey);
            const parsed = raw ? JSON.parse(raw) : null;
            const messages = Array.isArray(parsed?.messages)
                ? parsed.messages
                    .filter(item => item && ['user', 'assistant'].includes(item.role) && typeof item.content === 'string')
                    .slice(-maximumMessages)
                    .map(item => ({
                        role: item.role,
                        content: item.content,
                        products: normalizeProducts(item.products)
                    }))
                : [];
            return { messages };
        } catch {
            return { messages: [] };
        }
    }

    function saveState() {
        try {
            sessionStorage.setItem(storageKey, JSON.stringify({ messages: state.messages }));
        } catch {
            // Chat vẫn hoạt động nếu trình duyệt chặn sessionStorage.
        }
    }

    function safeRelativeUrl(value) {
        const url = String(value || '');
        return url.startsWith('/') && !url.startsWith('//')
            ? url
            : '/Store';
    }

    function safeImageUrl(value) {
        const url = String(value || '');
        if (url.startsWith('/') && !url.startsWith('//')) return url;
        if (url.startsWith('https://')) return url;
        return '/images/products/default-product.png';
    }

    function formatCurrency(value) {
        const number = Number(value || 0);
        return new Intl.NumberFormat('vi-VN', {
            style: 'currency',
            currency: 'VND',
            maximumFractionDigits: 0
        }).format(Number.isFinite(number) ? number : 0);
    }

    function parseInteger(value) {
        const parsed = Number.parseInt(String(value ?? ''), 10);
        return Number.isFinite(parsed) ? parsed : 0;
    }

    function track(eventName, data) {
        try {
            window.phoneCommerceAnalytics?.track(eventName, data || {});
        } catch {
            // Analytics không được làm gián đoạn chat.
        }
    }
})();
