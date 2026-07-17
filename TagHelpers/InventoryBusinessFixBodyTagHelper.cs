using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.AspNetCore.Mvc.ViewFeatures;
using Microsoft.AspNetCore.Razor.TagHelpers;
using System;
using System.Text.Encodings.Web;

namespace TMDT_LT.TagHelpers;

/// <summary>
/// Gắn lớp UI hiệu chỉnh cho đúng các route kho cũ mà không phải thay toàn bộ view.
/// </summary>
[HtmlTargetElement("body")]
public sealed class InventoryBusinessFixBodyTagHelper : TagHelper
{
    [HtmlAttributeNotBound]
    [ViewContext]
    public ViewContext ViewContext { get; set; } = null!;

    public override int Order => 900;

    public override void Process(
        TagHelperContext context,
        TagHelperOutput output)
    {
        string area = ViewContext.RouteData.Values["area"]?.ToString() ?? string.Empty;
        string controller = ViewContext.RouteData.Values["controller"]?.ToString() ?? string.Empty;
        string action = ViewContext.RouteData.Values["action"]?.ToString() ?? string.Empty;

        if (!area.Equals("Admin", StringComparison.OrdinalIgnoreCase)
            || !controller.Equals("Inventory", StringComparison.OrdinalIgnoreCase)
            || action is not ("Index" or "CreateSO" or "CreatePO"))
        {
            return;
        }

        string encodedAction = HtmlEncoder.Default.Encode(action);
        output.Attributes.SetAttribute("data-kp-inventory-action", encodedAction);
        output.PostContent.AppendHtml(
            "<script defer src=\"/js/admin/inventory-business-fix.js?v=3.0.0\"></script>");
    }
}
