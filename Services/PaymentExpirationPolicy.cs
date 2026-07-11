using Microsoft.Extensions.Options;
using System;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

public sealed class PaymentExpirationPolicy
{
    private readonly IOptionsMonitor<UnpaidOrderExpirationOptions> _options;

    public PaymentExpirationPolicy(IOptionsMonitor<UnpaidOrderExpirationOptions> options)
    {
        _options = options;
    }

    public UnpaidOrderExpirationOptions Current => _options.CurrentValue;

    public DateTime? GetDeadline(Payments payment)
    {
        var referenceTime = payment.CreatedAt;
        if (string.Equals(payment.LastResponseCode, "RETRY", StringComparison.Ordinal)
            && payment.LastProcessedAt.HasValue
            && payment.LastProcessedAt.Value > referenceTime)
        {
            referenceTime = payment.LastProcessedAt.Value;
        }

        return GetDeadline(payment.PaymentMethod, referenceTime);
    }

    public DateTime? GetDeadline(string? paymentMethod, DateTime createdAt)
    {
        var options = Current;

        if (string.Equals(paymentMethod, PaymentMethods.VnPay, StringComparison.OrdinalIgnoreCase))
        {
            return createdAt.AddMinutes(Math.Max(1, options.VnPayTimeoutMinutes));
        }

        if (string.Equals(paymentMethod, PaymentMethods.BankTransfer, StringComparison.OrdinalIgnoreCase))
        {
            return createdAt.AddMinutes(Math.Max(1, options.BankTransferTimeoutMinutes));
        }

        return null;
    }

    public string? GetAwaitingStatus(string? paymentMethod)
    {
        if (string.Equals(paymentMethod, PaymentMethods.VnPay, StringComparison.OrdinalIgnoreCase))
        {
            return PaymentStatuses.AwaitingGateway;
        }

        if (string.Equals(paymentMethod, PaymentMethods.BankTransfer, StringComparison.OrdinalIgnoreCase))
        {
            return PaymentStatuses.AwaitingBankTransfer;
        }

        return null;
    }

    public bool IsExpired(Payments payment, DateTime now)
    {
        var awaitingStatus = GetAwaitingStatus(payment.PaymentMethod);
        var deadline = GetDeadline(payment);

        return awaitingStatus != null
            && deadline.HasValue
            && string.Equals(payment.PaymentStatus, awaitingStatus, StringComparison.Ordinal)
            && now >= deadline.Value;
    }
}
