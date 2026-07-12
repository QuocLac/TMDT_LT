using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Areas.Admin.Controllers;

[Area("Admin")]
[Authorize(Roles = "Admin")]
public sealed class InvoiceRequestController : Controller
{
    private readonly ApplicationDbContext _context;

    public InvoiceRequestController(
        ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Index(
        string? status,
        string? search,
        CancellationToken cancellationToken)
    {
        IQueryable<OrderInvoiceRequests> query =
            _context.OrderInvoiceRequests
                .AsNoTracking()
                .Include(current => current.Order);

        if (!string.IsNullOrWhiteSpace(status)
            && IsKnownStatus(status))
        {
            query = query.Where(current =>
                current.Status == status);
        }

        if (!string.IsNullOrWhiteSpace(search))
        {
            string normalized = search.Trim();

            if (int.TryParse(
                    normalized,
                    out int orderId))
            {
                query = query.Where(current =>
                    current.OrderId == orderId);
            }
            else
            {
                query = query.Where(current =>
                    current.BuyerName.Contains(normalized)
                    || (current.TaxCode != null
                        && current.TaxCode.Contains(normalized))
                    || current.BuyerEmail.Contains(normalized));
            }
        }

        ViewBag.Status = status;
        ViewBag.Search = search;

        var requests = await query
            .OrderBy(current =>
                current.Status
                    == InvoiceRequestStatuses.Pending
                        ? 0
                        : 1)
            .ThenByDescending(current =>
                current.RequestedAt)
            .Take(300)
            .ToListAsync(cancellationToken);

        return View(requests);
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> MarkIssued(
        int id,
        string invoiceNumber,
        string? invoiceLookupCode,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(invoiceNumber))
        {
            TempData["Error"] =
                "Vui lòng nhập số hoặc ký hiệu hóa đơn đã phát hành.";
            return RedirectToAction(nameof(Index));
        }

        OrderInvoiceRequests? request =
            await _context.OrderInvoiceRequests
                .FirstOrDefaultAsync(
                    current =>
                        current.InvoiceRequestId == id,
                    cancellationToken);

        if (request == null)
        {
            return NotFound();
        }

        if (request.Status
            != InvoiceRequestStatuses.Pending)
        {
            TempData["Error"] =
                "Yêu cầu này đã được xử lý trước đó.";
            return RedirectToAction(nameof(Index));
        }

        request.Status =
            InvoiceRequestStatuses.Issued;
        request.InvoiceNumber =
            Truncate(invoiceNumber, 100);
        request.InvoiceLookupCode =
            Truncate(invoiceLookupCode, 200);
        request.IssuedAt = DateTime.UtcNow;
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewedBy =
            Truncate(User.Identity?.Name, 120);
        request.AdminNote = null;

        try
        {
            await _context.SaveChangesAsync(
                cancellationToken);
            TempData["Success"] =
                "Đã ghi nhận hóa đơn được phát hành.";
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["Error"] =
                "Yêu cầu vừa được người khác cập nhật. Vui lòng tải lại trang.";
        }

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Reject(
        int id,
        string adminNote,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(adminNote))
        {
            TempData["Error"] =
                "Vui lòng nhập lý do từ chối.";
            return RedirectToAction(nameof(Index));
        }

        OrderInvoiceRequests? request =
            await _context.OrderInvoiceRequests
                .FirstOrDefaultAsync(
                    current =>
                        current.InvoiceRequestId == id,
                    cancellationToken);

        if (request == null)
        {
            return NotFound();
        }

        if (request.Status
            != InvoiceRequestStatuses.Pending)
        {
            TempData["Error"] =
                "Yêu cầu này đã được xử lý trước đó.";
            return RedirectToAction(nameof(Index));
        }

        request.Status =
            InvoiceRequestStatuses.Rejected;
        request.AdminNote =
            Truncate(adminNote, 500);
        request.ReviewedAt = DateTime.UtcNow;
        request.ReviewedBy =
            Truncate(User.Identity?.Name, 120);

        try
        {
            await _context.SaveChangesAsync(
                cancellationToken);
            TempData["Success"] =
                "Đã từ chối yêu cầu xuất hóa đơn.";
        }
        catch (DbUpdateConcurrencyException)
        {
            TempData["Error"] =
                "Yêu cầu vừa được người khác cập nhật. Vui lòng tải lại trang.";
        }

        return RedirectToAction(nameof(Index));
    }

    private static bool IsKnownStatus(
        string status) =>
        status is InvoiceRequestStatuses.Pending
            or InvoiceRequestStatuses.Issued
            or InvoiceRequestStatuses.Rejected
            or InvoiceRequestStatuses.Cancelled;

    private static string? Truncate(
        string? value,
        int maxLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        string normalized = value.Trim();

        return normalized.Length <= maxLength
            ? normalized
            : normalized[..maxLength];
    }
}
