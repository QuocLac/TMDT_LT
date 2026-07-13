using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Antiforgery;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;

namespace TMDT_LT.TagHelpers;

[HtmlTargetElement("title")]
[HtmlTargetElement("h4")]
public sealed class KingPhoneBrandTagHelper : TagHelper
{
    public override async Task ProcessAsync(TagHelperContext context, TagHelperOutput output)
    {
        var childContent = await output.GetChildContentAsync();
        var content = childContent.GetContent();

        if (string.Equals(output.TagName, "title", StringComparison.OrdinalIgnoreCase))
        {
            output.Content.SetHtmlContent(
                content.Replace("PHONE.ST", "KingPhone", StringComparison.OrdinalIgnoreCase));
            return;
        }

        if (string.Equals(content.Trim(), "PHONE.ST STORE", StringComparison.OrdinalIgnoreCase))
        {
            output.Content.SetContent("KINGPHONE");
        }
    }
}

[HtmlTargetElement("head")]
public sealed class KingPhoneAiHeadTagHelper : TagHelper
{
    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (KingPhoneAiTagHelperGuard.IsAdmin(ViewContext))
        {
            return;
        }

        var pathBase = ViewContext.HttpContext.Request.PathBase.Value?.TrimEnd('/') ?? string.Empty;
        var href = HtmlEncoder.Default.Encode($"{pathBase}/css/shared/kingphone-ai-chat.css");
        output.PostContent.AppendHtml(
            $"<link rel=\"stylesheet\" href=\"{href}\" />");
    }
}

[HtmlTargetElement("body")]
public sealed class KingPhoneAiBodyTagHelper : TagHelper
{
    private readonly IAntiforgery _antiforgery;

    public KingPhoneAiBodyTagHelper(IAntiforgery antiforgery)
    {
        _antiforgery = antiforgery;
    }

    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    public override void Process(TagHelperContext context, TagHelperOutput output)
    {
        if (KingPhoneAiTagHelperGuard.IsAdmin(ViewContext))
        {
            return;
        }

        var httpContext = ViewContext.HttpContext;
        var pathBase = httpContext.Request.PathBase.Value?.TrimEnd('/') ?? string.Empty;
        var token = _antiforgery.GetAndStoreTokens(httpContext).RequestToken ?? string.Empty;
        var encodedToken = HtmlEncoder.Default.Encode(token);
        var endpoint = HtmlEncoder.Default.Encode($"{pathBase}/AiChat/Send");
        var scriptPath = HtmlEncoder.Default.Encode($"{pathBase}/js/shared/kingphone-ai-chat.js");

        output.PostContent.AppendHtml($$"""
            <section id="kingphone-ai-chat"
                     class="kp-ai-chat"
                     data-endpoint="{{endpoint}}"
                     data-antiforgery="{{encodedToken}}">
                <button type="button"
                        class="kp-ai-launcher"
                        aria-controls="kp-ai-panel"
                        aria-expanded="false"
                        aria-label="Mở Trợ lý mua sắm AI của KingPhone">
                    <span class="kp-ai-launcher-icon" aria-hidden="true">
                        <i class="fa-solid fa-wand-magic-sparkles"></i>
                    </span>
                    <span class="kp-ai-launcher-copy">
                        <strong>Trợ lý KingPhone</strong>
                        <small>Tư vấn mua sắm bằng AI</small>
                    </span>
                </button>

                <div id="kp-ai-panel"
                     class="kp-ai-panel"
                     role="dialog"
                     aria-modal="false"
                     aria-labelledby="kp-ai-title"
                     hidden>
                    <header class="kp-ai-header">
                        <div class="kp-ai-brand-mark" aria-hidden="true">
                            <i class="fa-solid fa-crown"></i>
                        </div>
                        <div class="kp-ai-header-copy">
                            <strong id="kp-ai-title">Trợ lý KingPhone</strong>
                            <span><i class="fa-solid fa-circle"></i> Tư vấn sản phẩm và mua hàng</span>
                        </div>
                        <button type="button" class="kp-ai-reset" title="Bắt đầu cuộc trò chuyện mới" aria-label="Bắt đầu cuộc trò chuyện mới">
                            <i class="fa-solid fa-rotate-right"></i>
                        </button>
                        <button type="button" class="kp-ai-close" aria-label="Thu nhỏ cửa sổ trò chuyện">
                            <i class="fa-solid fa-minus"></i>
                        </button>
                    </header>

                    <div class="kp-ai-context-note">
                        <i class="fa-solid fa-shield-halved"></i>
                        Giá, tồn kho và ưu đãi được kiểm tra trực tiếp từ dữ liệu KingPhone trước khi Trợ lý xác nhận.
                    </div>

                    <div class="kp-ai-messages" role="log" aria-live="polite" aria-relevant="additions">
                        <div class="kp-ai-message kp-ai-message-assistant">
                            <div class="kp-ai-avatar" aria-hidden="true"><i class="fa-solid fa-crown"></i></div>
                            <div class="kp-ai-bubble">Chào bạn, mình là Trợ lý KingPhone. Bạn đang ưu tiên ngân sách, hiệu năng, camera, pin hay thiết kế?</div>
                        </div>
                    </div>

                    <div class="kp-ai-quick-replies" aria-label="Gợi ý câu hỏi">
                        <button type="button" data-message="Tìm điện thoại còn hàng dưới 15 triệu tại KingPhone">Dưới 15 triệu</button>
                        <button type="button" data-message="So sánh hai sản phẩm phù hợp với nhu cầu của tôi">So sánh sản phẩm</button>
                        <button type="button" data-message="KingPhone đang có ưu đãi và Flash Sale nào?">Ưu đãi hiện tại</button>
                    </div>

                    <form class="kp-ai-form" novalidate>
                        <label class="visually-hidden" for="kp-ai-input">Nội dung cần tư vấn</label>
                        <textarea id="kp-ai-input"
                                  rows="1"
                                  maxlength="1200"
                                  placeholder="Nhập nhu cầu của bạn..."
                                  autocomplete="off"></textarea>
                        <button type="submit" class="kp-ai-send" aria-label="Gửi câu hỏi">
                            <i class="fa-solid fa-paper-plane"></i>
                        </button>
                    </form>
                    <div class="kp-ai-footnote">AI có thể đưa ra thông tin chưa hoàn toàn chính xác. Dữ liệu giao dịch sẽ được KingPhone kiểm tra lại trước khi xác nhận.</div>
                </div>
            </section>
            <script defer src="{{scriptPath}}"></script>
            """);
    }
}

internal static class KingPhoneAiTagHelperGuard
{
    public static bool IsAdmin(ViewContext viewContext)
    {
        var area = viewContext.RouteData.Values["area"]?.ToString();
        return string.Equals(area, "Admin", StringComparison.OrdinalIgnoreCase)
               || viewContext.HttpContext.Request.Path.StartsWithSegments("/Admin");
    }
}
