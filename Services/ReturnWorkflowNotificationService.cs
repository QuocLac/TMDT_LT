using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Net.Mail;
using System.Threading;
using System.Threading.Tasks;

namespace TMDT_LT.Services;

public sealed class ReturnWorkflowNotificationService
    : IReturnWorkflowNotificationService
{
    private readonly IConfiguration _configuration;
    private readonly ILogger<
        ReturnWorkflowNotificationService> _logger;

    public ReturnWorkflowNotificationService(
        IConfiguration configuration,
        ILogger<ReturnWorkflowNotificationService> logger)
    {
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendAsync(
        ReturnWorkflowNotification notification,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(
                notification.CustomerEmail)
            || notification.Kind
                == ReturnWorkflowNotificationKinds.None)
        {
            return;
        }

        IConfigurationSection settings =
            _configuration.GetSection(
                "SmtpSettings");

        string? senderEmail =
            settings["SenderEmail"];
        string? senderName =
            settings["SenderName"];
        string? server =
            settings["Server"];
        string? password =
            settings["Password"];

        if (string.IsNullOrWhiteSpace(senderEmail)
            || string.IsNullOrWhiteSpace(server)
            || !int.TryParse(
                settings["Port"],
                out int port))
        {
            return;
        }

        string customerName = WebUtility.HtmlEncode(
            string.IsNullOrWhiteSpace(
                notification.CustomerName)
                ? "Quý khách"
                : notification.CustomerName.Trim());
        string adminNote = WebUtility.HtmlEncode(
            notification.AdminNote);
        string orderCode =
            $"#ORD-{notification.OrderId}";

        (string subject, string message) =
            BuildMessage(
                notification.Kind,
                orderCode);

        string body =
            $"<h3>Chào {customerName},</h3>"
            + $"<p>{message}</p>"
            + $"<p><b>Đơn hàng:</b> "
            + $"{WebUtility.HtmlEncode(orderCode)}</p>"
            + $"<p><b>Ghi chú từ cửa hàng:</b> "
            + $"{adminNote}</p>";

        try
        {
            using var mailMessage =
                new MailMessage
                {
                    From = new MailAddress(
                        senderEmail,
                        senderName ?? "PHONE.ST"),
                    Subject = subject,
                    Body = body,
                    IsBodyHtml = true
                };

            mailMessage.To.Add(
                notification.CustomerEmail);

            using var smtpClient =
                new SmtpClient(server)
                {
                    Port = port,
                    Credentials =
                        new NetworkCredential(
                            senderEmail,
                            password
                                ?? string.Empty),
                    EnableSsl = true
                };

            cancellationToken
                .ThrowIfCancellationRequested();

            await smtpClient.SendMailAsync(
                mailMessage);
        }
        catch (OperationCanceledException ex)
        {
            // Nghiệp vụ đã commit trước khi gửi email.
            _logger.LogInformation(
                ex,
                "Email cập nhật trả hàng bị hủy sau commit cho OrderId {OrderId}.",
                notification.OrderId);
        }
        catch (Exception ex)
        {
            // Email là side effect sau commit; không rollback nghiệp vụ.
            _logger.LogWarning(
                ex,
                "Không gửi được email cập nhật trả hàng cho OrderId {OrderId}.",
                notification.OrderId);
        }
    }

    private static (string Subject, string Message)
        BuildMessage(
            string kind,
            string orderCode) =>
        kind switch
        {
            ReturnWorkflowNotificationKinds.Accepted =>
                (
                    $"[PHONE.ST] Yêu cầu trả hàng {orderCode} được chấp nhận",
                    "Yêu cầu trả hàng đã được chấp nhận. "
                    + "Vui lòng thực hiện bước gửi sản phẩm về cửa hàng theo hướng dẫn."
                ),
            ReturnWorkflowNotificationKinds.Inspecting =>
                (
                    $"[PHONE.ST] Đã nhận hàng hoàn {orderCode}",
                    "Cửa hàng đã nhận sản phẩm hoàn và bắt đầu kiểm định."
                ),
            ReturnWorkflowNotificationKinds.Rejected =>
                (
                    $"[PHONE.ST] Cập nhật yêu cầu trả hàng {orderCode}",
                    "Yêu cầu trả hàng chưa được chấp thuận."
                ),
            _ =>
                (
                    $"[PHONE.ST] Cập nhật trả hàng {orderCode}",
                    "Tiến trình trả hàng vừa được cập nhật."
                )
        };
}
