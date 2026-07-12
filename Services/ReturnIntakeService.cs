using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Data;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Services;

public sealed class ReturnIntakeService
    : IReturnIntakeService
{
    private static readonly HashSet<string>
        AllowedContentTypes =
            new(StringComparer.OrdinalIgnoreCase)
            {
                "image/jpeg",
                "image/png",
                "image/webp",
                "video/mp4",
                "video/quicktime"
            };

    private readonly ApplicationDbContext _context;
    private readonly IWebHostEnvironment _environment;
    private readonly IOrderStateService _orderStateService;
    private readonly ReturnIntakeOptions _options;
    private readonly ILogger<ReturnIntakeService> _logger;

    public ReturnIntakeService(
        ApplicationDbContext context,
        IWebHostEnvironment environment,
        IOrderStateService orderStateService,
        IOptions<ReturnIntakeOptions> options,
        ILogger<ReturnIntakeService> logger)
    {
        _context = context;
        _environment = environment;
        _orderStateService = orderStateService;
        _options = options.Value;
        _logger = logger;
    }

    public async Task<ReturnIntakeResult> SubmitAsync(
        ReturnIntakeCommand command,
        CancellationToken cancellationToken = default)
    {
        ReturnInput input;
        try
        {
            input = ValidateAndNormalize(command);
        }
        catch (ReturnIntakeValidationException ex)
        {
            return Failure(ex.Message);
        }

        List<string> savedPhysicalFiles = [];

        await using var transaction =
            await _context.Database.BeginTransactionAsync(
                IsolationLevel.Serializable,
                cancellationToken);

        try
        {
            Orders? order = await _context.Orders
                .Include(current => current.OrderHistories)
                .Include(current => current.Payments)
                .Include(current => current.OrderReturns)
                .Include(current => current.Shipping)
                .FirstOrDefaultAsync(
                    current =>
                        current.OrderId == command.OrderId
                        && current.CustomerId
                            == command.CustomerId,
                    cancellationToken);

            if (order == null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Failure(
                    "Không tìm thấy đơn hàng hoặc bạn không có quyền gửi yêu cầu.");
            }

            OrderReturns? activeReturn = order.OrderReturns
                .Where(current =>
                    IsActiveReturnStatus(current.Status))
                .OrderByDescending(current =>
                    current.CreatedAt)
                .FirstOrDefault();

            bool paymentStateRepaired =
                await RepairLegacyAwaitingRefundAsync(
                    order,
                    cancellationToken);

            if (activeReturn != null)
            {
                if (paymentStateRepaired)
                {
                    await _context.SaveChangesAsync(
                        cancellationToken);
                }

                await transaction.CommitAsync(
                    cancellationToken);

                return new ReturnIntakeResult(
                    true,
                    true,
                    "Đơn hàng đã có một hồ sơ đổi trả đang được xử lý.",
                    activeReturn.ReturnId,
                    paymentStateRepaired);
            }

            if (!string.Equals(
                    order.Status,
                    OrderStatuses.Delivered,
                    StringComparison.Ordinal))
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Failure(
                    "Chỉ có thể gửi yêu cầu trả hàng khi đơn đang ở trạng thái Đã giao.");
            }

            DateTime? deliveredAt =
                ResolveDeliveredAt(order);

            if (!deliveredAt.HasValue)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Failure(
                    "Không xác định được thời điểm giao hàng để kiểm tra thời hạn đổi trả.");
            }

            DateTime now = DateTime.Now;
            int returnWindowDays = Math.Clamp(
                _options.ReturnWindowDays,
                1,
                30);

            if (deliveredAt.Value > now.AddMinutes(5))
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Failure(
                    "Mốc giao hàng không hợp lệ. Vui lòng liên hệ cửa hàng.");
            }

            if (now - deliveredAt.Value
                > TimeSpan.FromDays(returnWindowDays))
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return Failure(
                    $"Đã quá thời hạn {returnWindowDays} ngày đổi trả.");
            }

            Payments? payment = order.Payments
                .OrderByDescending(current =>
                    current.PaymentId)
                .FirstOrDefault();

            ReturnIntakeResult? paymentValidation =
                ValidatePaymentAndRefundDestination(
                    payment,
                    input);

            if (paymentValidation != null)
            {
                await transaction.RollbackAsync(
                    cancellationToken);

                return paymentValidation;
            }

            string mediaPaths = await SaveEvidenceFilesAsync(
                command.Files,
                savedPhysicalFiles,
                cancellationToken);

            var returnRecord = new OrderReturns
            {
                OrderId = order.OrderId,
                CustomerId = command.CustomerId,
                Reason = input.Reason,
                Description = input.Description,
                MediaUrls = mediaPaths,
                Status = ReturnStatuses.Pending,
                CreatedAt = now,
                RefundBankCode = input.BankCode,
                RefundAccountNumber =
                    input.AccountNumber,
                RefundAccountName = input.AccountName
            };

            _context.OrderReturns.Add(returnRecord);

            _orderStateService.Transition(
                order,
                OrderStatuses.ReturnPending,
                $"Khách hàng mở yêu cầu trả hàng. "
                + $"Lý do: {input.Reason}.",
                now);

            // Không đổi Payments.Paid sang AwaitingRefund tại bước tiếp nhận.
            // RefundSettlementService chỉ đổi trạng thái khi hồ sơ đã Inspecting
            // và lệnh hoàn tiền thật sự được chuẩn bị.
            await _context.SaveChangesAsync(
                cancellationToken);
            await transaction.CommitAsync(
                cancellationToken);

            return new ReturnIntakeResult(
                true,
                false,
                "Đã gửi hồ sơ trả hàng thành công. Trạng thái thanh toán chỉ thay đổi khi yêu cầu được duyệt và bắt đầu hoàn tiền.",
                returnRecord.ReturnId,
                paymentStateRepaired);
        }
        catch (ReturnIntakeValidationException ex)
        {
            await transaction.RollbackAsync(
                cancellationToken);
            DeleteSavedFiles(savedPhysicalFiles);

            return Failure(ex.Message);
        }
        catch (DbUpdateConcurrencyException ex)
        {
            await transaction.RollbackAsync(
                cancellationToken);
            DeleteSavedFiles(savedPhysicalFiles);

            _logger.LogWarning(
                ex,
                "Xung đột khi tiếp nhận trả hàng cho OrderId {OrderId}.",
                command.OrderId);

            return Failure(
                "Đơn hàng vừa được cập nhật bởi một tiến trình khác. Vui lòng tải lại trang.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(
                cancellationToken);
            DeleteSavedFiles(savedPhysicalFiles);

            _logger.LogError(
                ex,
                "Không thể tiếp nhận trả hàng cho OrderId {OrderId}.",
                command.OrderId);

            return Failure(
                "Không thể gửi hồ sơ trả hàng. Vui lòng thử lại sau.");
        }
    }

    private ReturnInput ValidateAndNormalize(
        ReturnIntakeCommand command)
    {
        if (command.CustomerId <= 0)
        {
            throw new ReturnIntakeValidationException(
                "Không xác định được tài khoản khách hàng.");
        }

        if (command.OrderId <= 0)
        {
            throw new ReturnIntakeValidationException(
                "Mã đơn hàng không hợp lệ.");
        }

        string reason = RequiredText(
            command.Reason,
            "Lý do trả hàng",
            3,
            255);

        string? description = OptionalText(
            command.Description,
            2000);

        IReadOnlyList<IFormFile> files =
            command.Files
                ?? Array.Empty<IFormFile>();

        ValidateFiles(files);

        return new ReturnInput(
            reason,
            description,
            NormalizeBankCode(command.BankCode),
            NormalizeAccountNumber(
                command.AccountNumber),
            NormalizeAccountName(
                command.AccountName));
    }

    private void ValidateFiles(
        IReadOnlyList<IFormFile> files)
    {
        IFormFile[] actualFiles = files
            .Where(current =>
                current != null
                && current.Length > 0)
            .ToArray();

        int maxFiles = Math.Clamp(
            _options.MaxFiles,
            1,
            10);

        if (actualFiles.Length > maxFiles)
        {
            throw new ReturnIntakeValidationException(
                $"Chỉ được tải tối đa {maxFiles} tệp minh chứng.");
        }

        if (_options.RequireEvidence
            && actualFiles.Length == 0)
        {
            throw new ReturnIntakeValidationException(
                "Vui lòng tải ít nhất một ảnh hoặc video minh chứng.");
        }

        long maxFileSize = Math.Max(
            1024 * 1024,
            _options.MaxFileSizeBytes);
        long maxTotalSize = Math.Max(
            maxFileSize,
            _options.MaxTotalFileSizeBytes);

        long totalSize = 0;

        HashSet<string> allowedExtensions =
            new(
                (_options.AllowedExtensions
                    ?? Array.Empty<string>())
                .Select(NormalizeExtension)
                .Where(current =>
                    !string.IsNullOrWhiteSpace(current)),
                StringComparer.OrdinalIgnoreCase);

        foreach (IFormFile file in actualFiles)
        {
            if (file.Length > maxFileSize)
            {
                throw new ReturnIntakeValidationException(
                    $"Tệp '{Path.GetFileName(file.FileName)}' vượt quá giới hạn {FormatMegabytes(maxFileSize)} MB.");
            }

            totalSize += file.Length;
            if (totalSize > maxTotalSize)
            {
                throw new ReturnIntakeValidationException(
                    $"Tổng dung lượng minh chứng vượt quá {FormatMegabytes(maxTotalSize)} MB.");
            }

            string extension = NormalizeExtension(
                Path.GetExtension(file.FileName));

            if (!allowedExtensions.Contains(extension)
                || !AllowedContentTypes.Contains(
                    file.ContentType
                        ?? string.Empty))
            {
                throw new ReturnIntakeValidationException(
                    "Chỉ chấp nhận JPG, PNG, WEBP, MP4 hoặc MOV.");
            }
        }
    }

    private ReturnIntakeResult?
        ValidatePaymentAndRefundDestination(
            Payments? payment,
            ReturnInput input)
    {
        if (payment == null)
        {
            return Failure(
                "Không tìm thấy thông tin thanh toán của đơn hàng.");
        }

        bool isVnPay = string.Equals(
            payment.PaymentMethod,
            PaymentMethods.VnPay,
            StringComparison.OrdinalIgnoreCase);

        bool isCod = string.Equals(
            payment.PaymentMethod,
            PaymentMethods.Cod,
            StringComparison.OrdinalIgnoreCase);

        if (!isCod
            && payment.PaymentStatus
                != PaymentStatuses.Paid)
        {
            return Failure(
                "Khoản thanh toán của đơn chưa ở trạng thái có thể tiếp nhận hoàn trả.");
        }

        if (!isVnPay)
        {
            if (string.IsNullOrWhiteSpace(
                    input.BankCode)
                || string.IsNullOrWhiteSpace(
                    input.AccountNumber)
                || string.IsNullOrWhiteSpace(
                    input.AccountName))
            {
                return Failure(
                    "Vui lòng nhập đầy đủ ngân hàng, số tài khoản và tên chủ tài khoản nhận hoàn tiền.");
            }
        }

        return null;
    }

    private async Task<bool>
        RepairLegacyAwaitingRefundAsync(
            Orders order,
            CancellationToken cancellationToken)
    {
        Payments? payment = order.Payments
            .OrderByDescending(current =>
                current.PaymentId)
            .FirstOrDefault();

        if (payment?.PaymentStatus
            != PaymentStatuses.AwaitingRefund)
        {
            return false;
        }

        bool hasActiveRefundEvent =
            await _context.PaymentTransactions
                .AsNoTracking()
                .AnyAsync(
                    current =>
                        current.OrderId == order.OrderId
                        && current.EventType
                            == PaymentEventTypes.Refund
                        && current.Status
                            != PaymentEventStatuses.Failed
                        && current.Status
                            != PaymentEventStatuses.Rejected,
                    cancellationToken);

        if (hasActiveRefundEvent)
        {
            throw new ReturnIntakeValidationException(
                "Đơn hàng đang có lệnh hoàn tiền hoặc cần đối soát. Không thể mở thêm hồ sơ.");
        }

        payment.PaymentStatus =
            PaymentStatuses.Paid;
        payment.FailureReason = null;
        payment.LastProcessedAt = DateTime.Now;
        payment.PaymentDate ??= DateTime.Now;

        return true;
    }

    private DateTime? ResolveDeliveredAt(
        Orders order)
    {
        DateTime? historyTime =
            order.OrderHistories
                .Where(current =>
                    current.Status
                        == OrderStatuses.Delivered)
                .OrderByDescending(current =>
                    current.UpdatedAt)
                .Select(current =>
                    (DateTime?)current.UpdatedAt)
                .FirstOrDefault();

        if (historyTime.HasValue)
        {
            return historyTime;
        }

        return order.Shipping
            .Where(current =>
                current.DeliveredDate.HasValue)
            .OrderByDescending(current =>
                current.DeliveredDate)
            .Select(current =>
                current.DeliveredDate)
            .FirstOrDefault();
    }

    private async Task<string> SaveEvidenceFilesAsync(
        IReadOnlyList<IFormFile> files,
        ICollection<string> savedPhysicalFiles,
        CancellationToken cancellationToken)
    {
        IFormFile[] actualFiles = files
            .Where(current =>
                current != null
                && current.Length > 0)
            .ToArray();

        if (actualFiles.Length == 0)
        {
            return string.Empty;
        }

        string webRoot =
            _environment.WebRootPath
            ?? Path.Combine(
                _environment.ContentRootPath,
                "wwwroot");

        string returnFolder = Path.Combine(
            webRoot,
            "uploads",
            "returns");

        Directory.CreateDirectory(returnFolder);

        List<string> publicPaths = [];

        foreach (IFormFile file in actualFiles)
        {
            string extension = NormalizeExtension(
                Path.GetExtension(file.FileName));
            string targetName =
                $"{Guid.NewGuid():N}{extension}";
            string physicalPath = Path.Combine(
                returnFolder,
                targetName);

            await using var stream = new FileStream(
                physicalPath,
                FileMode.CreateNew,
                FileAccess.Write,
                FileShare.None,
                81920,
                useAsync: true);

            await file.CopyToAsync(
                stream,
                cancellationToken);

            savedPhysicalFiles.Add(physicalPath);
            publicPaths.Add(
                $"/uploads/returns/{targetName}");
        }

        return string.Join(",", publicPaths);
    }

    private static bool IsActiveReturnStatus(
        string? status) =>
        status == ReturnStatuses.Pending
        || status == ReturnStatuses.AwaitingCustomer
        || status == ReturnStatuses.Inspecting;

    private static string RequiredText(
        string? value,
        string fieldName,
        int minLength,
        int maxLength)
    {
        string normalized =
            value?.Trim()
            ?? string.Empty;

        if (normalized.Length < minLength)
        {
            throw new ReturnIntakeValidationException(
                $"{fieldName} phải có ít nhất {minLength} ký tự.");
        }

        if (normalized.Length > maxLength)
        {
            throw new ReturnIntakeValidationException(
                $"{fieldName} vượt quá {maxLength} ký tự.");
        }

        return normalized;
    }

    private static string? OptionalText(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();

        if (normalized.Length > maxLength)
        {
            throw new ReturnIntakeValidationException(
                $"Nội dung mô tả vượt quá {maxLength} ký tự.");
        }

        return normalized;
    }

    private static string? NormalizeBankCode(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized =
            value.Trim().ToUpperInvariant();

        if (!Regex.IsMatch(
                normalized,
                @"^[A-Z0-9_-]{2,20}$"))
        {
            throw new ReturnIntakeValidationException(
                "Mã ngân hàng không hợp lệ.");
        }

        return normalized;
    }

    private static string? NormalizeAccountNumber(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = Regex.Replace(
            value,
            @"[\s.-]",
            string.Empty);

        if (!Regex.IsMatch(
                normalized,
                @"^[0-9]{6,30}$"))
        {
            throw new ReturnIntakeValidationException(
                "Số tài khoản nhận hoàn tiền không hợp lệ.");
        }

        return normalized;
    }

    private static string? NormalizeAccountName(
        string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = Regex.Replace(
            value.Trim(),
            @"\s+",
            " ");

        if (normalized.Length < 2
            || normalized.Length > 100)
        {
            throw new ReturnIntakeValidationException(
                "Tên chủ tài khoản nhận hoàn tiền không hợp lệ.");
        }

        return normalized.ToUpperInvariant();
    }

    private static string NormalizeExtension(
        string? extension)
    {
        if (string.IsNullOrWhiteSpace(extension))
        {
            return string.Empty;
        }

        string normalized =
            extension.Trim().ToLowerInvariant();

        return normalized.StartsWith('.')
            ? normalized
            : $".{normalized}";
    }

    private static int FormatMegabytes(
        long bytes) =>
        Math.Max(
            1,
            (int)Math.Ceiling(
                bytes / 1024d / 1024d));

    private static void DeleteSavedFiles(
        IEnumerable<string> paths)
    {
        foreach (string path in paths)
        {
            try
            {
                if (File.Exists(path))
                {
                    File.Delete(path);
                }
            }
            catch
            {
                // Không che lỗi nghiệp vụ chính nếu dọn file thất bại.
            }
        }
    }

    private static ReturnIntakeResult Failure(
        string message) =>
        new(
            false,
            false,
            message);

    private sealed record ReturnInput(
        string Reason,
        string? Description,
        string? BankCode,
        string? AccountNumber,
        string? AccountName);

    private sealed class
        ReturnIntakeValidationException
        : InvalidOperationException
    {
        public ReturnIntakeValidationException(
            string message)
            : base(message)
        {
        }
    }
}
