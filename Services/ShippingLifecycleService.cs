using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using System;
using System.Globalization;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

public sealed class ShippingLifecycleService : IShippingLifecycleService
{
    private readonly ApplicationDbContext _context;
    private readonly GhnService _ghnService;
    private readonly IOrderStateService _orderStateService;
    private readonly ILogger<ShippingLifecycleService> _logger;

    public ShippingLifecycleService(
        ApplicationDbContext context,
        GhnService ghnService,
        IOrderStateService orderStateService,
        ILogger<ShippingLifecycleService> logger)
    {
        _context = context;
        _ghnService = ghnService;
        _orderStateService = orderStateService;
        _logger = logger;
    }

    public async Task<ShippingCreationResult> EnsureShipmentCreatedAsync(
        int orderId,
        CancellationToken cancellationToken = default)
    {
        EnsureTransaction();

        var order = await _context.Orders
            .Include(current => current.OrderDetails)
                .ThenInclude(detail => detail.Variant)
                    .ThenInclude(variant => variant!.Product)
            .Include(current => current.Payments)
            .Include(current => current.Shipping)
            .FirstOrDefaultAsync(current => current.OrderId == orderId, cancellationToken)
            ?? throw new InvalidOperationException($"Không tìm thấy đơn hàng #{orderId}.");

        var shipping = order.Shipping
            .OrderByDescending(current => current.ShippingId)
            .FirstOrDefault();

        if (shipping != null && !string.IsNullOrWhiteSpace(shipping.TrackingNumber))
        {
            return new ShippingCreationResult(
                false,
                shipping.ProviderCode ?? string.Empty,
                shipping.TrackingNumber,
                $"Vận đơn {shipping.TrackingNumber} đã được tạo trước đó.");
        }

        var carrier = await _context.ShippingCarriers
            .Where(current => current.IsActive)
            .OrderByDescending(current => current.IsDefault)
            .ThenByDescending(current => current.CarrierCode == "GHN")
            .FirstOrDefaultAsync(cancellationToken);

        shipping ??= new Shipping
        {
            OrderId = order.OrderId,
            CreatedAt = DateTime.Now
        };

        if (!order.Shipping.Contains(shipping))
        {
            order.Shipping.Add(shipping);
        }

        if (carrier == null)
        {
            shipping.Carrier = "Chưa phân công";
            shipping.ProviderCode = "MANUAL";
            shipping.Status = ShippingStatuses.AwaitingManual;
            shipping.UpdatedAt = DateTime.Now;

            return new ShippingCreationResult(
                false,
                "MANUAL",
                null,
                "Chưa có đơn vị vận chuyển mặc định; đơn đang chờ tạo vận đơn thủ công.");
        }

        shipping.Carrier = carrier.CarrierName;
        shipping.ProviderCode = carrier.CarrierCode;

        if (!string.Equals(carrier.CarrierCode, "GHN", StringComparison.OrdinalIgnoreCase))
        {
            shipping.Status = ShippingStatuses.AwaitingManual;
            shipping.UpdatedAt = DateTime.Now;

            return new ShippingCreationResult(
                false,
                carrier.CarrierCode,
                null,
                $"Đơn vị {carrier.CarrierName} chưa có API tự động; đang chờ tạo vận đơn thủ công.");
        }

        if (!_ghnService.IsConfigured)
        {
            throw new InvalidOperationException(
                "GHN chưa được cấu hình đủ BaseUrl, Token và ShopId.");
        }

        int? districtId = order.ShippingDistrictId
            ?? ParseLeadingInt(order.ShippingDistrict);
        string? wardCode = FirstSegment(order.ShippingWardCode)
            ?? FirstSegment(order.ShippingWard);

        if (!districtId.HasValue || districtId.Value <= 0
            || string.IsNullOrWhiteSpace(wardCode))
        {
            throw new InvalidOperationException(
                $"Đơn #{order.OrderId} thiếu mã quận/huyện hoặc mã phường/xã GHN.");
        }

        string recipientName = order.ShippingFullName?.Trim() ?? string.Empty;
        string recipientPhone = order.ShippingPhone?.Trim() ?? string.Empty;
        string recipientAddress = JoinAddress(
            order.ShippingStreet,
            order.ShippingWard,
            order.ShippingDistrict,
            order.ShippingCity);

        if (string.IsNullOrWhiteSpace(recipientName)
            || string.IsNullOrWhiteSpace(recipientPhone)
            || string.IsNullOrWhiteSpace(recipientAddress))
        {
            throw new InvalidOperationException(
                $"Đơn #{order.OrderId} thiếu thông tin người nhận để tạo vận đơn.");
        }

        var payment = order.Payments
            .OrderByDescending(current => current.PaymentId)
            .FirstOrDefault();

        int codAmount = payment != null
            && string.Equals(
                payment.PaymentMethod,
                PaymentMethods.Cod,
                StringComparison.OrdinalIgnoreCase)
            && payment.PaymentStatus != PaymentStatuses.Paid
                ? ToIntAmount(order.TotalAmount)
                : 0;

        int insuranceValue = Math.Min(
            ToIntAmount(order.TotalAmount),
            _ghnService.MaxInsuranceValue);
        int totalWeight = Math.Max(
            500,
            order.OrderDetails.Sum(detail => Math.Max(0, detail.Quantity ?? 0) * 500));

        var items = order.OrderDetails
            .Where(detail => (detail.Quantity ?? 0) > 0)
            .Select(detail => new GhnItem(
                Name: !string.IsNullOrWhiteSpace(detail.ProductNameSnapshot)
                    ? detail.ProductNameSnapshot
                    : detail.Variant?.Product?.Name
                        ?? $"Sản phẩm #{detail.VariantId}",
                Code: !string.IsNullOrWhiteSpace(detail.VariantCodeSnapshot)
                    ? detail.VariantCodeSnapshot
                    : $"VAR-{detail.VariantId}",
                Quantity: detail.Quantity ?? 1,
                Price: ToIntAmount(detail.UnitPrice),
                Weight: 500))
            .ToArray();

        if (items.Length == 0)
        {
            throw new InvalidOperationException(
                $"Đơn #{order.OrderId} không có sản phẩm hợp lệ để tạo vận đơn.");
        }

        DateTime now = DateTime.Now;
        GhnCreateShippingResult result = await _ghnService.CreateShippingOrderAsync(
            new GhnCreateShippingRequest(
                order.OrderId,
                recipientName,
                recipientPhone,
                recipientAddress,
                wardCode,
                districtId.Value,
                totalWeight,
                codAmount,
                insuranceValue,
                items),
            cancellationToken);

        shipping.TrackingNumber = result.OrderCode;
        shipping.ProviderStatus = "ready_to_pick";
        shipping.Status = ShippingStatuses.Created;
        shipping.EstimatedDelivery = result.ExpectedDeliveryTime;
        shipping.ShippingFee = result.TotalFee ?? order.ShippingFee;
        shipping.CodAmount = codAmount;
        shipping.InsuranceValue = insuranceValue;
        shipping.ServiceTypeId = _ghnService.ServiceTypeId;
        shipping.UpdatedAt = now;
        shipping.LastError = null;
        shipping.Note = $"GHN client order code: ORD-{order.OrderId}";

        string eventKey = $"GHN:CREATE:{order.OrderId}:{result.OrderCode}";
        bool eventExists = await _context.ShippingEvents
            .AnyAsync(current => current.IdempotencyKey == eventKey, cancellationToken);

        if (!eventExists)
        {
            _context.ShippingEvents.Add(new ShippingEvents
            {
                Shipping = shipping,
                Order = order,
                OrderId = order.OrderId,
                Provider = "GHN",
                EventType = ShippingEventTypes.Created,
                IdempotencyKey = eventKey,
                TrackingNumber = result.OrderCode,
                ProviderStatus = shipping.ProviderStatus,
                MappedStatus = shipping.Status,
                Status = ShippingEventStatuses.Processed,
                ReceivedAt = now,
                ProcessedAt = now
            });
        }

        return new ShippingCreationResult(
            true,
            "GHN",
            result.OrderCode,
            $"Đã tạo vận đơn GHN {result.OrderCode}.");
    }

    public async Task SyncManualOrderStatusAsync(
        int orderId,
        string orderStatus,
        DateTime? occurredAt = null,
        CancellationToken cancellationToken = default)
    {
        EnsureTransaction();

        var shipping = await _context.Shipping
            .Include(current => current.Order)
            .Where(current => current.OrderId == orderId)
            .OrderByDescending(current => current.ShippingId)
            .FirstOrDefaultAsync(cancellationToken);

        if (shipping == null)
        {
            shipping = new Shipping
            {
                OrderId = orderId,
                Carrier = "Chưa phân công",
                ProviderCode = "MANUAL",
                Status = ShippingStatuses.Pending,
                CreatedAt = occurredAt ?? DateTime.Now
            };
            _context.Shipping.Add(shipping);
        }

        DateTime now = occurredAt ?? DateTime.Now;
        string mappedStatus;
        string providerStatus;
        string eventType = ShippingEventTypes.ManualSync;

        if (orderStatus == OrderStatuses.Shipping)
        {
            mappedStatus = ShippingStatuses.InTransit;
            providerStatus = "manual_in_transit";
            shipping.ShippedDate ??= now;
        }
        else if (orderStatus == OrderStatuses.Delivered)
        {
            mappedStatus = ShippingStatuses.Delivered;
            providerStatus = "manual_delivered";
            shipping.ShippedDate ??= now;
            shipping.DeliveredDate ??= now;
        }
        else if (orderStatus == OrderStatuses.Cancelled)
        {
            if (string.Equals(
                    shipping.ProviderCode,
                    "GHN",
                    StringComparison.OrdinalIgnoreCase)
                && !string.IsNullOrWhiteSpace(shipping.TrackingNumber)
                && shipping.Status != ShippingStatuses.Cancelled)
            {
                await _ghnService.CancelShippingOrderAsync(
                    shipping.TrackingNumber,
                    cancellationToken);
            }

            mappedStatus = ShippingStatuses.Cancelled;
            providerStatus = "manual_cancelled";
            eventType = ShippingEventTypes.Cancelled;
            shipping.CancelledAt ??= now;
        }
        else
        {
            return;
        }

        shipping.Status = mappedStatus;
        shipping.ProviderStatus = providerStatus;
        shipping.UpdatedAt = now;
        shipping.LastError = null;

        string eventKey = $"LOCAL:SHIPPING:{orderId}:{orderStatus}";
        bool eventExists = await _context.ShippingEvents
            .AnyAsync(current => current.IdempotencyKey == eventKey, cancellationToken);

        if (!eventExists)
        {
            _context.ShippingEvents.Add(new ShippingEvents
            {
                Shipping = shipping,
                OrderId = orderId,
                Provider = shipping.ProviderCode ?? "MANUAL",
                EventType = eventType,
                IdempotencyKey = eventKey,
                TrackingNumber = shipping.TrackingNumber,
                ProviderStatus = providerStatus,
                MappedStatus = mappedStatus,
                Status = ShippingEventStatuses.Processed,
                ReceivedAt = now,
                ProcessedAt = now
            });
        }
    }

    public async Task<ShippingWebhookResult> ProcessGhnWebhookAsync(
        string rawPayload,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(rawPayload))
        {
            return new ShippingWebhookResult(false, false, null, "Webhook không có dữ liệu.");
        }

        using var document = JsonDocument.Parse(rawPayload);
        JsonElement payload = ResolvePayload(document.RootElement);
        string? orderCode = ReadString(
            payload,
            "OrderCode",
            "order_code",
            "orderCode");
        string? providerStatus = ReadString(
            payload,
            "Status",
            "status");
        string? eventTimeRaw = ReadString(
            payload,
            "Time",
            "time",
            "UpdatedDate",
            "updated_date");

        if (string.IsNullOrWhiteSpace(orderCode)
            || string.IsNullOrWhiteSpace(providerStatus))
        {
            return new ShippingWebhookResult(
                false,
                false,
                null,
                "Webhook GHN thiếu OrderCode hoặc Status.");
        }

        string normalizedStatus = providerStatus.Trim().ToLowerInvariant();
        DateTime eventTime = ParseEventTime(eventTimeRaw) ?? DateTime.Now;
        string payloadHash = ComputeSha256(rawPayload);
        string idempotencyKey =
            $"GHN:WEBHOOK:{orderCode}:{normalizedStatus}:{payloadHash[..16]}";

        using var transaction = await _context.Database.BeginTransactionAsync(
            System.Data.IsolationLevel.Serializable,
            cancellationToken);

        try
        {
            var shipping = await _context.Shipping
                .Include(current => current.Order)
                    .ThenInclude(order => order!.Payments)
                .FirstOrDefaultAsync(
                    current => current.TrackingNumber == orderCode,
                    cancellationToken);

            if (shipping?.Order == null)
            {
                await transaction.RollbackAsync(cancellationToken);
                return new ShippingWebhookResult(
                    false,
                    false,
                    null,
                    $"Không tìm thấy vận đơn {orderCode}.");
            }

            int inserted = await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                  INSERT INTO ShippingEvents
                  (
                      ShippingId,
                      OrderId,
                      Provider,
                      EventType,
                      IdempotencyKey,
                      TrackingNumber,
                      ProviderStatus,
                      PayloadHash,
                      Status,
                      ReceivedAt
                  )
                  SELECT
                      {shipping.ShippingId},
                      {shipping.OrderId!.Value},
                      {"GHN"},
                      {ShippingEventTypes.Webhook},
                      {idempotencyKey},
                      {orderCode},
                      {normalizedStatus},
                      {payloadHash},
                      {ShippingEventStatuses.Received},
                      {eventTime}
                  WHERE NOT EXISTS
                  (
                      SELECT 1
                      FROM ShippingEvents WITH (UPDLOCK, HOLDLOCK)
                      WHERE IdempotencyKey = {idempotencyKey}
                  )
                  """,
                cancellationToken);

            if (inserted == 0)
            {
                await transaction.CommitAsync(cancellationToken);
                return new ShippingWebhookResult(
                    true,
                    true,
                    shipping.OrderId,
                    "Webhook GHN đã được xử lý trước đó.");
            }

            string mappedStatus = MapGhnStatus(normalizedStatus);
            DateTime now = DateTime.Now;
            shipping.ProviderStatus = normalizedStatus;
            shipping.Status = mappedStatus;
            shipping.LastWebhookAt = eventTime;
            shipping.UpdatedAt = now;
            shipping.LastError = null;

            var order = shipping.Order;
            var payment = order.Payments
                .OrderByDescending(current => current.PaymentId)
                .FirstOrDefault();

            if (mappedStatus == ShippingStatuses.InTransit)
            {
                shipping.ShippedDate ??= eventTime;

                if (order.Status == OrderStatuses.Processing)
                {
                    _orderStateService.Transition(
                        order,
                        OrderStatuses.Shipping,
                        $"GHN cập nhật vận đơn {orderCode}: {normalizedStatus}.",
                        eventTime);
                }
            }
            else if (mappedStatus == ShippingStatuses.Delivered)
            {
                shipping.ShippedDate ??= eventTime;
                shipping.DeliveredDate ??= eventTime;

                if (order.Status == OrderStatuses.Processing)
                {
                    _orderStateService.Transition(
                        order,
                        OrderStatuses.Shipping,
                        $"GHN đã tiếp nhận vận chuyển đơn #{order.OrderId}.",
                        eventTime);
                }

                if (order.Status == OrderStatuses.Shipping)
                {
                    _orderStateService.Transition(
                        order,
                        OrderStatuses.Delivered,
                        $"GHN xác nhận giao thành công vận đơn {orderCode}.",
                        eventTime);
                }

                if (payment != null
                    && string.Equals(
                        payment.PaymentMethod,
                        PaymentMethods.Cod,
                        StringComparison.OrdinalIgnoreCase)
                    && payment.PaymentStatus != PaymentStatuses.Paid)
                {
                    payment.PaymentStatus = PaymentStatuses.Paid;
                    payment.PaymentDate = eventTime;
                    payment.Amount = order.TotalAmount;
                    payment.ProviderTransactionId = orderCode;
                    payment.LastTransactionStatus = normalizedStatus;
                    payment.LastResponseCode = "GHN_DELIVERED";
                    payment.LastProcessedAt = now;
                    payment.FailureReason = null;

                    string codEventKey = $"GHN:COD:{orderCode}:DELIVERED";
                    bool codEventExists = await _context.PaymentTransactions
                        .AnyAsync(
                            current => current.IdempotencyKey == codEventKey,
                            cancellationToken);

                    if (!codEventExists)
                    {
                        _context.PaymentTransactions.Add(new PaymentTransactions
                        {
                            PaymentId = payment.PaymentId,
                            OrderId = order.OrderId,
                            Provider = "GHN_COD",
                            EventType = PaymentEventTypes.CodCollected,
                            IdempotencyKey = codEventKey,
                            ProviderTransactionId = orderCode,
                            Amount = order.TotalAmount,
                            Status = PaymentEventStatuses.Processed,
                            ResponseCode = "DELIVERED",
                            TransactionStatus = normalizedStatus,
                            PayloadHash = payloadHash,
                            ReceivedAt = eventTime,
                            ProcessedAt = now
                        });
                    }
                }
            }
            else if (mappedStatus == ShippingStatuses.DeliveryFailed
                || mappedStatus == ShippingStatuses.Returning
                || mappedStatus == ShippingStatuses.Returned
                || mappedStatus == ShippingStatuses.Cancelled
                || mappedStatus == ShippingStatuses.Exception)
            {
                _context.OrderHistories.Add(new OrderHistory
                {
                    OrderId = order.OrderId,
                    Status = order.Status ?? OrderStatuses.Processing,
                    UpdatedAt = eventTime,
                    Note = $"[GHN] Vận đơn {orderCode}: {mappedStatus} ({normalizedStatus})."
                });
            }

            await _context.SaveChangesAsync(cancellationToken);

            await _context.Database.ExecuteSqlInterpolatedAsync(
                $"""
                  UPDATE ShippingEvents
                  SET Status = {ShippingEventStatuses.Processed},
                      MappedStatus = {mappedStatus},
                      ProcessedAt = {now}
                  WHERE IdempotencyKey = {idempotencyKey}
                  """,
                cancellationToken);

            await transaction.CommitAsync(cancellationToken);

            return new ShippingWebhookResult(
                true,
                false,
                order.OrderId,
                $"Đã cập nhật vận đơn {orderCode} sang '{mappedStatus}'.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            _logger.LogError(
                ex,
                "Không thể xử lý webhook GHN cho vận đơn {OrderCode}.",
                orderCode);
            throw;
        }
    }

    private void EnsureTransaction()
    {
        if (_context.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                "Nghiệp vụ vận chuyển phải chạy bên trong database transaction.");
        }
    }

    private static int? ParseLeadingInt(string? value)
    {
        string? first = FirstSegment(value);
        return int.TryParse(first, out int parsed) ? parsed : null;
    }

    private static string? FirstSegment(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        return value.Split('|', 2, StringSplitOptions.TrimEntries)[0];
    }

    private static string JoinAddress(params string?[] parts) =>
        string.Join(
            ", ",
            parts.Where(part => !string.IsNullOrWhiteSpace(part))
                .Select(part => part!.Trim()));

    private static int ToIntAmount(decimal? value)
    {
        decimal normalized = Math.Clamp(
            decimal.Truncate(value ?? 0),
            0,
            int.MaxValue);
        return decimal.ToInt32(normalized);
    }

    private static JsonElement ResolvePayload(JsonElement root)
    {
        foreach (string name in new[] { "Data", "data" })
        {
            if (root.TryGetProperty(name, out JsonElement nested)
                && nested.ValueKind == JsonValueKind.Object)
            {
                return nested;
            }
        }

        return root;
    }

    private static string? ReadString(
        JsonElement element,
        params string[] propertyNames)
    {
        foreach (string propertyName in propertyNames)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
            {
                continue;
            }

            if (value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }

            if (value.ValueKind == JsonValueKind.Number
                || value.ValueKind == JsonValueKind.True
                || value.ValueKind == JsonValueKind.False)
            {
                return value.GetRawText();
            }
        }

        return null;
    }

    private static DateTime? ParseEventTime(string? raw)
    {
        if (DateTimeOffset.TryParse(
                raw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out DateTimeOffset parsed))
        {
            return parsed.LocalDateTime;
        }

        return null;
    }

    private static string ComputeSha256(string payload)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string MapGhnStatus(string status) =>
        status switch
        {
            "ready_to_pick" => ShippingStatuses.Created,
            "picking" or "money_collect_picking" => ShippingStatuses.Picking,
            "picked"
                or "storing"
                or "transporting"
                or "sorting"
                or "delivering"
                or "money_collect_delivering" => ShippingStatuses.InTransit,
            "delivered" => ShippingStatuses.Delivered,
            "delivery_fail"
                or "waiting_to_return" => ShippingStatuses.DeliveryFailed,
            "return"
                or "return_transporting"
                or "return_sorting"
                or "returning" => ShippingStatuses.Returning,
            "returned" => ShippingStatuses.Returned,
            "cancel" or "cancelled" => ShippingStatuses.Cancelled,
            "exception"
                or "damage"
                or "lost"
                or "warehouse_damage" => ShippingStatuses.Exception,
            _ => ShippingStatuses.Unknown
        };
}
