using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using TMDT_LT.Data;

namespace TMDT_LT.Controllers;

/// <summary>
/// Handles HTTP 403 responses and refreshes authorization claims from the
/// account record. It never promotes an account to Admin unless the database
/// already stores the Admin role.
/// </summary>
[Route("Home")]
public sealed class AccessController : Controller
{
    private readonly ApplicationDbContext _context;
    private readonly ILogger<AccessController> _logger;

    public AccessController(
        ApplicationDbContext context,
        ILogger<AccessController> logger)
    {
        _context = context;
        _logger = logger;
    }

    [AllowAnonymous]
    [HttpGet("AccessDenied")]
    public IActionResult Denied(
        string? returnUrl = null,
        string? reason = null)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;

        ViewBag.ReturnUrl = IsSafeLocalUrl(returnUrl)
            ? returnUrl
            : null;
        ViewBag.Reason = reason;
        ViewBag.IsAuthenticated =
            User.Identity?.IsAuthenticated == true;
        ViewBag.CurrentRole = User
            .FindFirst(ClaimTypes.Role)
            ?.Value
            ?.Trim()
            ?? "Chưa xác định";

        return View("~/Views/Access/Denied.cshtml");
    }

    [Authorize]
    [HttpPost("RefreshAccess")]
    [ValidateAntiForgeryToken]
    public async Task<IActionResult> Refresh(
        string? returnUrl,
        CancellationToken cancellationToken)
    {
        string? accountIdValue = User
            .FindFirst(ClaimTypes.NameIdentifier)
            ?.Value;

        if (!int.TryParse(accountIdValue, out int accountId)
            || accountId <= 0)
        {
            await SignOutAsync();
            return RedirectToAction(
                "Login",
                "Auth",
                new
                {
                    returnUrl = IsSafeLocalUrl(returnUrl)
                        ? returnUrl
                        : null
                });
        }

        var account = await _context.Account
            .AsNoTracking()
            .Where(item =>
                item.AccountId == accountId
                && item.IsActive == true)
            .Select(item => new
            {
                item.AccountId,
                item.Email,
                item.Role
            })
            .FirstOrDefaultAsync(cancellationToken);

        if (account == null)
        {
            await SignOutAsync();
            return RedirectToAction(
                "Login",
                "Auth",
                new
                {
                    returnUrl = IsSafeLocalUrl(returnUrl)
                        ? returnUrl
                        : null
                });
        }

        string canonicalRole = CanonicalizeRole(account.Role);
        List<Claim> refreshedClaims = User.Claims
            .Where(claim => claim.Type != ClaimTypes.Role)
            .ToList();

        if (!refreshedClaims.Any(claim =>
                claim.Type == ClaimTypes.NameIdentifier))
        {
            refreshedClaims.Add(new Claim(
                ClaimTypes.NameIdentifier,
                account.AccountId.ToString()));
        }

        if (!refreshedClaims.Any(claim =>
                claim.Type == ClaimTypes.Email))
        {
            refreshedClaims.Add(new Claim(
                ClaimTypes.Email,
                account.Email));
        }

        refreshedClaims.Add(new Claim(
            ClaimTypes.Role,
            canonicalRole));

        var identity = new ClaimsIdentity(
            refreshedClaims,
            CookieAuthenticationDefaults.AuthenticationScheme,
            ClaimTypes.Name,
            ClaimTypes.Role);

        await HttpContext.SignInAsync(
            CookieAuthenticationDefaults.AuthenticationScheme,
            new ClaimsPrincipal(identity),
            new AuthenticationProperties
            {
                IsPersistent = true,
                AllowRefresh = true,
                ExpiresUtc = DateTimeOffset.UtcNow.AddDays(7)
            });

        _logger.LogInformation(
            "Authorization claims refreshed for AccountId={AccountId}; Role={Role}.",
            account.AccountId,
            canonicalRole);

        if (string.Equals(canonicalRole, "Admin", StringComparison.Ordinal))
        {
            return IsSafeLocalUrl(returnUrl)
                ? LocalRedirect(returnUrl!)
                : RedirectToAction(
                    "Index",
                    "Dashboard",
                    new { area = "Admin" });
        }

        return RedirectToAction(
            nameof(Denied),
            new
            {
                returnUrl = IsSafeLocalUrl(returnUrl)
                    ? returnUrl
                    : null,
                reason =
                    "Tài khoản hiện không có role Admin trong cơ sở dữ liệu."
            });
    }

    private async Task SignOutAsync()
    {
        await HttpContext.SignOutAsync(
            CookieAuthenticationDefaults.AuthenticationScheme);
    }

    private bool IsSafeLocalUrl(string? url)
    {
        return !string.IsNullOrWhiteSpace(url)
            && Url.IsLocalUrl(url);
    }

    private static string CanonicalizeRole(string? role)
    {
        string normalized = (role ?? string.Empty).Trim();

        if (string.Equals(
                normalized,
                "Admin",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Admin";
        }

        if (string.Equals(
                normalized,
                "Customer",
                StringComparison.OrdinalIgnoreCase))
        {
            return "Customer";
        }

        return string.IsNullOrWhiteSpace(normalized)
            ? "Customer"
            : normalized;
    }
}
