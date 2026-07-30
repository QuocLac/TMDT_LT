using System.Net;
using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TMDT_LT.Models.AI;
using TMDT_LT.Services.AI;

namespace TMDT_LT.Controllers;

[Route("AiChat")]
[ResponseCache(
    NoStore = true,
    Location = ResponseCacheLocation.None)]
public sealed class AiChatController : Controller
{
    private const int MaximumMessageLength = 1_200;
    private const int MaximumHistoryItems = 8;

    private readonly IKingPhoneAiService _aiService;
    private readonly IWebHostEnvironment _environment;

    public AiChatController(
        IKingPhoneAiService aiService,
        IWebHostEnvironment environment)
    {
        _aiService = aiService;
        _environment = environment;
    }

    [HttpGet("Health")]
    public IActionResult Health()
    {
        if (!_environment.IsDevelopment()
            && !IsLocalRequest())
        {
            return NotFound();
        }

        var status =
            _aiService.GetConfigurationStatus();

        return Json(new
        {
            status.Enabled,
            status.IsConfigured,
            status.ApiKeyConfigured,
            status.ApiKeySource,
            status.Provider,
            status.Model,
            status.BaseUrl,
            status.LegacyOpenAiConfigDetected,
            environment =
                _environment.EnvironmentName
        });
    }

    [HttpPost("Send")]
    [ValidateAntiForgeryToken]
    [EnableRateLimiting("kingphone-ai-chat")]
    public async Task<IActionResult> Send(
        [FromBody]
        KingPhoneAiChatRequest? request,
        CancellationToken cancellationToken)
    {
        if (request == null
            || string.IsNullOrWhiteSpace(
                request.Message))
        {
            return BadRequest(
                new KingPhoneAiChatResponse
                {
                    Success = false,
                    ErrorCode =
                        "EMPTY_MESSAGE",
                    Message =
                        "Bạn hãy nhập nội dung cần KingPhone tư vấn."
                });
        }

        request.Message =
            request.Message.Trim();

        if (request.Message.Length
            > MaximumMessageLength)
        {
            return BadRequest(
                new KingPhoneAiChatResponse
                {
                    Success = false,
                    ErrorCode =
                        "MESSAGE_TOO_LONG",
                    Message =
                        $"Nội dung tư vấn không được vượt quá {MaximumMessageLength:N0} ký tự."
                });
        }

        request.PagePath =
            Limit(request.PagePath, 300);
        request.PageTitle =
            Limit(request.PageTitle, 200);

        request.History =
            (request.History
             ?? new List<
                 KingPhoneAiHistoryMessage>())
            .Where(item =>
                item != null)
            .TakeLast(
                MaximumHistoryItems)
            .Select(item =>
                new KingPhoneAiHistoryMessage
                {
                    Role =
                        Limit(
                            item.Role,
                            20),
                    Content =
                        Limit(
                            item.Content,
                            MaximumMessageLength)
                })
            .ToList();

        var customerId =
            GetCurrentCustomerId();

        var result =
            await _aiService.ReplyAsync(
                request,
                customerId,
                cancellationToken);

        var response =
            new KingPhoneAiChatResponse
            {
                Success =
                    result.Success,
                Message =
                    result.Message,
                ResponseId =
                    result.ResponseId,
                ErrorCode =
                    result.ErrorCode,
                Products =
                    result.Products,
                ToolsUsed =
                    result.ToolsUsed,
                QuickReplies =
                    result.QuickReplies.Count > 0
                        ? result
                            .QuickReplies
                        : result.Success
                            ? new List<string>
                            {
                                "Tìm sản phẩm theo ngân sách",
                                "So sánh hai sản phẩm",
                                "Kiểm tra ưu đãi hiện tại"
                            }
                            : new List<string>()
            };

        if (!result.IsConfigured)
        {
            return StatusCode(
                StatusCodes
                    .Status503ServiceUnavailable,
                response);
        }

        return result.Success
            ? Json(response)
            : StatusCode(
                StatusCodes
                    .Status502BadGateway,
                response);
    }

    private bool IsLocalRequest()
    {
        var remoteIp =
            HttpContext.Connection.RemoteIpAddress;

        return remoteIp != null
            && IPAddress.IsLoopback(remoteIp);
    }

    private int?
        GetCurrentCustomerId()
    {
        if (User.Identity
                ?.IsAuthenticated
            != true)
        {
            return null;
        }

        var value =
            User.FindFirst(
                "CustomerId")
                ?.Value
            ?? User.FindFirst(
                ClaimTypes
                    .NameIdentifier)
                ?.Value;

        return int.TryParse(
                   value,
                   out var customerId)
               && customerId > 0
            ? customerId
            : null;
    }

    private static string Limit(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(
                value))
        {
            return string.Empty;
        }

        var trimmed = value.Trim();

        return trimmed.Length
            <= maxLength
                ? trimmed
                : trimmed[..maxLength];
    }
}
