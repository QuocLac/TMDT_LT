using Microsoft.EntityFrameworkCore;
using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

public sealed class PaymentTransactionService : IPaymentTransactionService
{
    private readonly ApplicationDbContext _context;

    public PaymentTransactionService(ApplicationDbContext context)
    {
        _context = context;
    }

    public async Task<bool> TryClaimAsync(
        PaymentEventClaim claim,
        CancellationToken cancellationToken = default)
    {
        EnsureTransaction();

        if (string.IsNullOrWhiteSpace(claim.IdempotencyKey))
        {
            throw new ArgumentException("Idempotency key không được để trống.", nameof(claim));
        }

        var receivedAt = claim.ReceivedAt ?? DateTime.Now;
        var payloadHash = ComputeSha256(claim.RawPayload);

        var inserted = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
              INSERT INTO PaymentTransactions
              (
                  PaymentId,
                  OrderId,
                  Provider,
                  EventType,
                  IdempotencyKey,
                  ProviderTransactionId,
                  Amount,
                  Status,
                  ResponseCode,
                  TransactionStatus,
                  PayloadHash,
                  ReceivedAt
              )
              SELECT
                  {claim.PaymentId},
                  {claim.OrderId},
                  {claim.Provider},
                  {claim.EventType},
                  {claim.IdempotencyKey},
                  {claim.ProviderTransactionId},
                  {claim.Amount},
                  {PaymentEventStatuses.Received},
                  {claim.ResponseCode},
                  {claim.TransactionStatus},
                  {payloadHash},
                  {receivedAt}
              WHERE NOT EXISTS
              (
                  SELECT 1
                  FROM PaymentTransactions WITH (UPDLOCK, HOLDLOCK)
                  WHERE IdempotencyKey = {claim.IdempotencyKey}
              )
              """,
            cancellationToken);

        return inserted == 1;
    }

    public async Task CompleteAsync(
        string idempotencyKey,
        string status,
        string? errorMessage = null,
        DateTime? processedAt = null,
        CancellationToken cancellationToken = default)
    {
        EnsureTransaction();

        var timestamp = processedAt ?? DateTime.Now;
        var affected = await _context.Database.ExecuteSqlInterpolatedAsync(
            $"""
              UPDATE PaymentTransactions
              SET Status = {status},
                  ErrorMessage = {errorMessage},
                  ProcessedAt = {timestamp}
              WHERE IdempotencyKey = {idempotencyKey}
              """,
            cancellationToken);

        if (affected != 1)
        {
            throw new InvalidOperationException(
                $"Không tìm thấy payment event '{idempotencyKey}' để hoàn tất.");
        }
    }

    private void EnsureTransaction()
    {
        if (_context.Database.CurrentTransaction == null)
        {
            throw new InvalidOperationException(
                "Payment event phải được xử lý bên trong database transaction.");
        }
    }

    private static string? ComputeSha256(string? payload)
    {
        if (string.IsNullOrEmpty(payload))
        {
            return null;
        }

        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(payload));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
