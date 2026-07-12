using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Threading;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels.Commerce;

namespace TMDT_LT.Controllers;

[Authorize]
[Route("orders/{orderId:int}/receipt")]
public sealed class OrderReceiptController : Controller
{
    private readonly ApplicationDbContext _context;

    public OrderReceiptController(
        ApplicationDbContext context)
    {
        _context = context;
    }

    [HttpGet]
    public async Task<IActionResult> Details(
        int orderId,
        CancellationToken cancellationToken)
    {
        bool isAdmin = User.IsInRole("Admin");
        int customerId = GetCurrentCustomerId();

        if (!isAdmin && customerId <= 0)
        {
            return Forbid();
        }

        Orders? order = await _context.Orders
            .AsNoTracking()
            .Include(current => current.OrderDetails)
                .ThenInclude(detail => detail.Variant)
                    .ThenInclude(variant => variant!.Product)
            .Include(current => current.Payments)
            .Include(current => current.Shipping)
            .FirstOrDefaultAsync(
                current => current.OrderId == orderId,
                cancellationToken);

        if (order == null)
        {
            return NotFound();
        }

        if (!isAdmin && order.CustomerId != customerId)
        {
            // Không tiết lộ việc OrderId của tài khoản khác có tồn tại.
            return NotFound();
        }

        OrderReceiptViewModel model =
            OrderReceiptViewModel.Create(
                order,
                isAdmin);

        return View(model);
    }

    private int GetCurrentCustomerId()
    {
        string? raw = User.FindFirstValue("CustomerId");

        return int.TryParse(raw, out int customerId)
            ? customerId
            : 0;
    }
}
