using Microsoft.AspNetCore.Razor.TagHelpers;
using System;

namespace TMDT_LT.TagHelpers;

[HtmlTargetElement(
    "form",
    Attributes = "id")]
public sealed class CheckoutInvoiceRequestFormTagHelper
    : TagHelper
{
    public override int Order => 180;

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

        string formId =
            idAttribute.Value?.ToString()
            ?? string.Empty;

        if (!formId.Equals(
                "formCheckout",
                StringComparison.Ordinal))
        {
            return;
        }

        output.PostContent.AppendHtml(
            """
            <link rel="stylesheet"
                  href="/css/shared/order-invoice-request.css?v=1.0.0" />

            <section class="checkout-invoice-request checkout-section-box"
                     data-checkout-invoice-request="true">
                <label class="checkout-invoice-request__toggle">
                    <input type="checkbox"
                           name="RequestVatInvoice"
                           value="true"
                           data-invoice-toggle />
                    <span>
                        <strong>Yêu cầu xuất hóa đơn điện tử</strong>
                        <small>
                            Thông tin được lưu cùng đơn hàng để bộ phận kế toán xử lý riêng.
                        </small>
                    </span>
                </label>

                <div class="checkout-invoice-request__fields"
                     data-invoice-fields
                     hidden>
                    <div class="checkout-invoice-request__notice">
                        Phiếu xác nhận đơn hàng không phải hóa đơn điện tử.
                        Hệ thống không tự phát hành số hóa đơn tại bước checkout.
                    </div>

                    <div class="checkout-invoice-request__grid">
                        <label>
                            <span>Loại người mua</span>
                            <select name="InvoiceBuyerType"
                                    data-invoice-input
                                    data-invoice-buyer-type>
                                <option value="Individual">
                                    Cá nhân
                                </option>
                                <option value="Organization">
                                    Tổ chức / doanh nghiệp
                                </option>
                            </select>
                        </label>

                        <label>
                            <span>Tên người mua / đơn vị</span>
                            <input type="text"
                                   name="InvoiceBuyerName"
                                   maxlength="200"
                                   autocomplete="organization"
                                   data-invoice-input
                                   required />
                        </label>

                        <label data-tax-code-field>
                            <span>
                                Mã số thuế
                                <em data-tax-code-required>
                                    Không bắt buộc với cá nhân
                                </em>
                            </span>
                            <input type="text"
                                   name="InvoiceTaxCode"
                                   maxlength="20"
                                   inputmode="numeric"
                                   autocomplete="off"
                                   data-invoice-input
                                   data-tax-code-input />
                        </label>

                        <label>
                            <span>Email nhận hóa đơn</span>
                            <input type="email"
                                   name="InvoiceEmail"
                                   maxlength="200"
                                   autocomplete="email"
                                   data-invoice-input
                                   required />
                        </label>

                        <label>
                            <span>Số điện thoại</span>
                            <input type="tel"
                                   name="InvoicePhone"
                                   maxlength="30"
                                   autocomplete="tel"
                                   data-invoice-input />
                        </label>

                        <label class="checkout-invoice-request__wide">
                            <span>Địa chỉ xuất hóa đơn</span>
                            <textarea name="InvoiceAddress"
                                      maxlength="500"
                                      rows="3"
                                      autocomplete="street-address"
                                      data-invoice-input
                                      required></textarea>
                        </label>
                    </div>
                </div>
            </section>

            <script src="/js/shared/order-invoice-request.js?v=1.0.0"
                    defer></script>
            """);
    }
}
