using Microsoft.AspNetCore.Razor.TagHelpers;
using System;

namespace TMDT_LT.TagHelpers;

[HtmlTargetElement(
    "form",
    Attributes = "id")]
public sealed class CheckoutIdempotencyFormTagHelper
    : TagHelper
{
    public override int Order => 100;

    public override void Process(
        TagHelperContext context,
        TagHelperOutput output)
    {
        if (!output.Attributes.TryGetAttribute(
                "id",
                out TagHelperAttribute? idAttribute))
        {
            return;
        }

        string id =
            idAttribute.Value?.ToString()
            ?? string.Empty;

        if (!id.Equals(
                "formCheckout",
                StringComparison.Ordinal))
        {
            return;
        }

        string key = Guid.NewGuid().ToString("N");

        output.PostContent.AppendHtml(
            $"""
             <input type="hidden"
                    name="CheckoutIdempotencyKey"
                    value="{key}" />
             """);
    }
}
