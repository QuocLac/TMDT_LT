using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
using TMDT_LT.Services;

namespace TMDT_LT.Controllers;

[ApiController]
[Route("api/shipping/ghn")]
public sealed class GhnShippingWebhookController : ControllerBase
{
    private readonly IShippingLifecycleService _shippingLifecycleService;
    private readonly IConfiguration _configuration;

    public GhnShippingWebhookController(
        IShippingLifecycleService shippingLifecycleService,
        IConfiguration configuration)
    {
        _shippingLifecycleService = shippingLifecycleService;
        _configuration = configuration;
    }

    [AllowAnonymous]
    [IgnoreAntiforgeryToken]
    [HttpPost("webhook")]
    public async Task<IActionResult> Webhook()
    {
        string configuredSecret =
            _configuration["ShippingAPI:GHN:WebhookSecret"] ?? string.Empty;

        // Sandbox có thể để trống secret. Khi cấu hình secret, request phải gửi
        // X-GHN-Webhook-Secret khớp với giá trị đó.
        if (!string.IsNullOrWhiteSpace(configuredSecret))
        {
            string providedSecret =
                Request.Headers["X-GHN-Webhook-Secret"].ToString();

            if (!string.Equals(
                    configuredSecret,
                    providedSecret,
                    StringComparison.Ordinal))
            {
                return Unauthorized(new
                {
                    success = false,
                    message = "GHN webhook secret không hợp lệ."
                });
            }
        }

        using var reader = new StreamReader(
            Request.Body,
            Encoding.UTF8,
            detectEncodingFromByteOrderMarks: true,
            leaveOpen: false);
        string rawPayload = await reader.ReadToEndAsync(
            HttpContext.RequestAborted);

        try
        {
            ShippingWebhookResult result =
                await _shippingLifecycleService.ProcessGhnWebhookAsync(
                    rawPayload,
                    HttpContext.RequestAborted);

            if (!result.Success)
            {
                return BadRequest(new
                {
                    success = false,
                    duplicate = result.Duplicate,
                    orderId = result.OrderId,
                    message = result.Message
                });
            }

            return Ok(new
            {
                success = true,
                duplicate = result.Duplicate,
                orderId = result.OrderId,
                message = result.Message
            });
        }
        catch
        {
            return StatusCode(500, new
            {
                success = false,
                message = "Không thể xử lý webhook GHN."
            });
        }
    }
}
