using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;

namespace TMDT_LT.Controllers
{
    public class AnalyticsEventDto
    {
        public string? EventName { get; set; }
        public string? SessionId { get; set; }
        public string? VisitorId { get; set; }
        public int? ProductId { get; set; }
        public int? VariantId { get; set; }
        public int? TargetProductId { get; set; }
        public int? OrderId { get; set; }
        public string? SearchKeyword { get; set; }
        public string? PagePath { get; set; }
        public string? Referrer { get; set; }
        public decimal? Value { get; set; }
        public int? Quantity { get; set; }
        public JsonElement? Metadata { get; set; }
    }

    [Route("Analytics")]
    public class AnalyticsController : Controller
    {
        private readonly ApplicationDbContext _context;

        public AnalyticsController(ApplicationDbContext context)
        {
            _context = context;
        }

        [HttpPost("Track")]
        public async Task<IActionResult> Track([FromBody] AnalyticsEventDto dto)
        {
            if (dto == null || string.IsNullOrWhiteSpace(dto.EventName))
            {
                return BadRequest(new { success = false, message = "Thiếu tên sự kiện." });
            }

            string eventName = NormalizeEventName(dto.EventName);
            string sessionKey = NormalizeKey(dto.SessionId, "sess");
            string visitorKey = NormalizeKey(dto.VisitorId, "vis");
            int? customerId = GetCurrentCustomerId();
            DateTime now = DateTime.UtcNow;

            var session = await _context.AnalyticsSessions.FirstOrDefaultAsync(s => s.SessionKey == sessionKey);
            if (session == null)
            {
                session = new AnalyticsSession
                {
                    SessionKey = sessionKey,
                    VisitorKey = visitorKey,
                    CustomerId = customerId,
                    FirstSeenAt = now,
                    LastSeenAt = now,
                    LandingPage = Safe(dto.PagePath, 600),
                    Referrer = Safe(dto.Referrer ?? Request.Headers["Referer"].ToString(), 600),
                    UserAgent = Safe(Request.Headers["User-Agent"].ToString(), 600),
                    IpHash = HashIp(HttpContext.Connection.RemoteIpAddress?.ToString()),
                    Source = Safe(Request.Query["utm_source"].ToString(), 120),
                    Medium = Safe(Request.Query["utm_medium"].ToString(), 120),
                    Campaign = Safe(Request.Query["utm_campaign"].ToString(), 120),
                    IsAuthenticated = customerId.HasValue
                };
                _context.AnalyticsSessions.Add(session);
            }
            else
            {
                session.LastSeenAt = now;
                session.VisitorKey = string.IsNullOrWhiteSpace(session.VisitorKey) ? visitorKey : session.VisitorKey;
                if (customerId.HasValue)
                {
                    session.CustomerId = customerId;
                    session.IsAuthenticated = true;
                }
            }

            session.EventCount++;
            if (eventName == "page_view_internal" || eventName == "page_view") session.PageViewCount++;
            if (eventName == "add_to_cart") session.HasAddToCart = true;
            if (eventName == "begin_checkout") session.HasCheckout = true;
            if (eventName == "purchase")
            {
                session.HasPurchase = true;
                session.TotalRevenue += dto.Value ?? 0m;
            }

            var analyticsEvent = new AnalyticsEvent
            {
                SessionKey = sessionKey,
                VisitorKey = visitorKey,
                CustomerId = customerId,
                EventName = eventName,
                ProductId = dto.ProductId,
                VariantId = dto.VariantId,
                TargetProductId = dto.TargetProductId,
                OrderId = dto.OrderId,
                SearchKeyword = Safe(dto.SearchKeyword, 300),
                PagePath = Safe(dto.PagePath, 600),
                Referrer = Safe(dto.Referrer ?? Request.Headers["Referer"].ToString(), 600),
                EventValue = dto.Value,
                Quantity = dto.Quantity,
                MetadataJson = dto.Metadata.HasValue ? dto.Metadata.Value.GetRawText() : null,
                CreatedAt = now
            };
            _context.AnalyticsEvents.Add(analyticsEvent);

            if ((eventName == "search" || eventName == "search_submit") && !string.IsNullOrWhiteSpace(dto.SearchKeyword))
            {
                _context.SearchQueryLogs.Add(new SearchQueryLog
                {
                    SessionKey = sessionKey,
                    VisitorKey = visitorKey,
                    CustomerId = customerId,
                    Keyword = Safe(dto.SearchKeyword, 300) ?? string.Empty,
                    NormalizedKeyword = NormalizeSearchText(dto.SearchKeyword),
                    ResultCount = TryReadInt(dto.Metadata, "resultCount"),
                    ClickedProductId = dto.TargetProductId,
                    CreatedAt = now
                });
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true });
        }

        [HttpGet("Recommendations")]
        public async Task<IActionResult> Recommendations(string? sessionId, string? visitorId, int take = 8, int? currentProductId = null, string? source = null)
        {
            take = Math.Clamp(take, 4, 24);
            string sessionKey = sessionId ?? string.Empty;
            string visitorKey = visitorId ?? string.Empty;
            int? customerId = GetCurrentCustomerId();
            var now = DateTime.Now;
            var cutoffAccount = DateTime.UtcNow.AddDays(-180);
            var cutoffSession = DateTime.UtcNow.AddDays(-30);

            var activeFlashSale = await _context.FlashSales
                .Include(f => f.FlashSaleItems)
                .Where(f => f.IsActive && f.StartTime <= now && f.EndTime >= now)
                .OrderByDescending(f => f.StartTime)
                .FirstOrDefaultAsync();

            var productAffinity = new Dictionary<int, decimal>();
            var brandAffinity = new Dictionary<int, decimal>();
            var categoryAffinity = new Dictionary<int, decimal>();
            var keywords = new List<string>();

            void AddProductScore(int? productId, decimal score)
            {
                if (!productId.HasValue || productId.Value <= 0) return;
                productAffinity[productId.Value] = productAffinity.TryGetValue(productId.Value, out var oldScore) ? oldScore + score : score;
            }

            // 1. Ưu tiên cao nhất: hành vi thuộc tài khoản đang đăng nhập.
            if (customerId.HasValue)
            {
                var accountEvents = await _context.AnalyticsEvents
                    .Where(e => e.CustomerId == customerId.Value && e.CreatedAt >= cutoffAccount)
                    .OrderByDescending(e => e.CreatedAt)
                    .Take(500)
                    .Select(e => new
                    {
                        e.EventName,
                        e.ProductId,
                        e.TargetProductId,
                        e.SearchKeyword,
                        e.Quantity,
                        e.CreatedAt
                    })
                    .ToListAsync();

                foreach (var e in accountEvents)
                {
                    decimal recency = RecencyBoost(e.CreatedAt, cutoffAccount, 1.35m);
                    decimal score = EventWeight(e.EventName) * recency;
                    AddProductScore(e.TargetProductId ?? e.ProductId, score);
                    if (!string.IsNullOrWhiteSpace(e.SearchKeyword)) keywords.Add(NormalizeSearchText(e.SearchKeyword));
                }

                var legacyLogs = await _context.UserBehaviorLogs
                    .Where(x => x.CustomerId == customerId.Value && x.CreatedAt >= cutoffAccount)
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(300)
                    .Select(x => new { x.ProductId, x.TargetProductId, x.ActionType, x.ViewDuration, x.SearchKeyword, x.CreatedAt })
                    .ToListAsync();

                foreach (var log in legacyLogs)
                {
                    decimal baseScore = log.ActionType == "ProductClick" ? 35m : Math.Clamp(log.ViewDuration / 3m, 6m, 30m);
                    AddProductScore(log.TargetProductId ?? log.ProductId, baseScore * RecencyBoost(log.CreatedAt, cutoffAccount, 1.2m));
                    if (!string.IsNullOrWhiteSpace(log.SearchKeyword)) keywords.Add(NormalizeSearchText(log.SearchKeyword));
                }
            }

            // 2. Nếu chưa đăng nhập hoặc cần bù ngữ cảnh mới nhất: thêm tín hiệu phiên/thiết bị với trọng số thấp hơn.
            if (!string.IsNullOrWhiteSpace(sessionKey) || !string.IsNullOrWhiteSpace(visitorKey))
            {
                var sessionEvents = await _context.AnalyticsEvents
                    .Where(e => e.CreatedAt >= cutoffSession)
                    .Where(e => (!string.IsNullOrWhiteSpace(sessionKey) && e.SessionKey == sessionKey) || (!string.IsNullOrWhiteSpace(visitorKey) && e.VisitorKey == visitorKey))
                    .OrderByDescending(e => e.CreatedAt)
                    .Take(220)
                    .Select(e => new
                    {
                        e.EventName,
                        e.ProductId,
                        e.TargetProductId,
                        e.SearchKeyword,
                        e.CreatedAt
                    })
                    .ToListAsync();

                foreach (var e in sessionEvents)
                {
                    decimal sessionMultiplier = customerId.HasValue ? 0.55m : 1.0m;
                    decimal score = EventWeight(e.EventName) * RecencyBoost(e.CreatedAt, cutoffSession, 1.0m) * sessionMultiplier;
                    AddProductScore(e.TargetProductId ?? e.ProductId, score);
                    if (!string.IsNullOrWhiteSpace(e.SearchKeyword)) keywords.Add(NormalizeSearchText(e.SearchKeyword));
                }
            }

            // 3. Chuyển product-affinity thành brand/category affinity để gợi ý sản phẩm liên quan, không chỉ lặp lại sản phẩm đã xem.
            var signalProductIds = productAffinity.Keys.ToList();
            if (signalProductIds.Any())
            {
                var profileProducts = await _context.Products
                    .Where(p => signalProductIds.Contains(p.ProductId))
                    .Select(p => new { p.ProductId, p.BrandId, p.CategoryId })
                    .ToListAsync();

                foreach (var item in profileProducts)
                {
                    decimal sourceScore = productAffinity.TryGetValue(item.ProductId, out var s) ? s : 0;
                    brandAffinity[item.BrandId] = brandAffinity.TryGetValue(item.BrandId, out var oldBrand) ? oldBrand + sourceScore * 0.42m : sourceScore * 0.42m;
                    categoryAffinity[item.CategoryId] = categoryAffinity.TryGetValue(item.CategoryId, out var oldCategory) ? oldCategory + sourceScore * 0.55m : sourceScore * 0.55m;
                }
            }

            var recentViews = await _context.AnalyticsEvents
                .Where(e => e.ProductId.HasValue && e.EventName == "view_item" && e.CreatedAt >= DateTime.UtcNow.AddDays(-30))
                .GroupBy(e => e.ProductId!.Value)
                .Select(g => new { ProductId = g.Key, Views = g.Count() })
                .ToDictionaryAsync(x => x.ProductId, x => x.Views);

            var cartSignals = await _context.AnalyticsEvents
                .Where(e => e.ProductId.HasValue && e.EventName == "add_to_cart" && e.CreatedAt >= DateTime.UtcNow.AddDays(-30))
                .GroupBy(e => e.ProductId!.Value)
                .Select(g => new { ProductId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ProductId, x => x.Count);

            var soldStats = await _context.OrderDetails
                .Where(d => d.Variant != null && d.Variant.ProductId > 0)
                .GroupBy(d => d.Variant!.ProductId)
                .Select(g => new { ProductId = g.Key, Qty = g.Sum(x => x.Quantity ?? 0) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Qty);

            var products = await _context.Products
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .Include(p => p.ProductVariants)
                .Where(p => p.IsActive == true)
                .ToListAsync();

            var normalizedKeywords = keywords
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct()
                .Take(10)
                .ToList();

            var suggestions = products
                .Where(p => p.ProductVariants.Any(v => v.IsActive == true))
                .Select(p =>
                {
                    decimal personalScore = productAffinity.TryGetValue(p.ProductId, out var ps) ? Math.Min(ps * 0.45m, 90m) : 0m;
                    decimal brandScore = brandAffinity.TryGetValue(p.BrandId, out var bs) ? Math.Min(bs, 110m) : 0m;
                    decimal categoryScore = categoryAffinity.TryGetValue(p.CategoryId, out var cs) ? Math.Min(cs, 130m) : 0m;
                    decimal keywordScore = KeywordMatchScore(p, normalizedKeywords);

                    decimal commerceScore = 10m;
                    if (IsInActiveFlashSale(p, activeFlashSale)) commerceScore += 38m;
                    if (HasSellableStock(p)) commerceScore += 35m; else commerceScore -= 180m;
                    if (HasDiscountPrice(p)) commerceScore += 20m;
                    if (recentViews.TryGetValue(p.ProductId, out int views)) commerceScore += Math.Min(views, 35);
                    if (cartSignals.TryGetValue(p.ProductId, out int carts)) commerceScore += Math.Min(carts * 3, 45);
                    if (soldStats.TryGetValue(p.ProductId, out int sold)) commerceScore += Math.Min(sold * 2, 70);

                    if (currentProductId.HasValue && p.ProductId == currentProductId.Value) commerceScore -= 500m;

                    decimal score = personalScore + brandScore + categoryScore + keywordScore + commerceScore;
                    string reason = BuildRecommendationReason(personalScore, brandScore, categoryScore, keywordScore, p, activeFlashSale);
                    return new { Product = p, Score = score, Reason = reason };
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => x.Product.CreatedDate)
                .Take(take)
                .Select(x =>
                {
                    var variant = GetBestActiveVariant(x.Product);
                    decimal price = variant == null ? 0m : GetDisplayPrice(variant);
                    return new
                    {
                        productId = x.Product.ProductId,
                        variantId = variant?.VariantId ?? 0,
                        name = x.Product.Name,
                        brand = x.Product.Brand?.BrandName ?? "",
                        category = x.Product.Category?.CategoryName ?? "",
                        imageUrl = !string.IsNullOrWhiteSpace(variant?.ImageUrl) ? variant.ImageUrl : (x.Product.MainImage ?? "/images/placeholder-product.png"),
                        price,
                        oldPrice = variant != null && variant.Price > price ? variant.Price : null,
                        stock = variant?.Stock ?? 0,
                        hasStock = (variant?.Stock ?? 0) > 0,
                        url = $"/Store/Product/{x.Product.ProductId}",
                        isFlashSale = IsInActiveFlashSale(x.Product, activeFlashSale),
                        score = Math.Round(x.Score, 2),
                        reason = x.Reason
                    };
                })
                .ToList();

            return Json(new
            {
                success = true,
                source = customerId.HasValue ? "account_behavior" : (!string.IsNullOrWhiteSpace(sessionKey) || !string.IsNullOrWhiteSpace(visitorKey) ? "session_behavior" : "market_fallback"),
                customerPersonalized = customerId.HasValue && (productAffinity.Any() || brandAffinity.Any() || categoryAffinity.Any()),
                formula = "score = product_action + brand/category_affinity + search_intent + promotion/stock + market_signal - penalties",
                items = suggestions
            });
        }

        private int? GetCurrentCustomerId()
        {
            if (User.Identity?.IsAuthenticated != true) return null;
            string userIdStr = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(ClaimTypes.NameIdentifier)?.Value ?? "0";
            return int.TryParse(userIdStr, out int id) && id > 0 ? id : null;
        }


        private static decimal EventWeight(string eventName)
        {
            eventName = NormalizeEventName(eventName);
            return eventName switch
            {
                "purchase" => 130m,
                "begin_checkout" => 85m,
                "add_to_cart" => 65m,
                "recommendation_click" => 48m,
                "product_click" => 38m,
                "view_item" => 24m,
                "search" => 12m,
                "search_submit" => 12m,
                "page_view_internal" => 4m,
                _ => 6m
            };
        }

        private static decimal RecencyBoost(DateTime createdAt, DateTime cutoff, decimal maxBoost)
        {
            var total = Math.Max((DateTime.UtcNow - cutoff).TotalDays, 1);
            var age = Math.Max((DateTime.UtcNow - createdAt).TotalDays, 0);
            var freshness = (decimal)Math.Max(0, 1 - (age / total));
            return 1m + freshness * maxBoost;
        }

        private static decimal KeywordMatchScore(Products product, List<string> normalizedKeywords)
        {
            if (normalizedKeywords == null || normalizedKeywords.Count == 0) return 0m;
            string text = NormalizeSearchText($"{product.Name} {product.Brand?.BrandName} {product.Category?.CategoryName}");
            decimal score = 0m;
            foreach (var keyword in normalizedKeywords)
            {
                if (string.IsNullOrWhiteSpace(keyword)) continue;
                var parts = keyword.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length == 0) continue;
                int matched = parts.Count(part => text.Contains(part));
                if (matched > 0) score += Math.Min(matched * 9m, 35m);
            }
            return Math.Min(score, 70m);
        }

        private static string BuildRecommendationReason(decimal personalScore, decimal brandScore, decimal categoryScore, decimal keywordScore, Products product, FlashSales? activeFlashSale)
        {
            if (personalScore >= 35m) return "Dựa trên sản phẩm bạn đã quan tâm";
            if (categoryScore >= 45m && brandScore >= 25m) return "Cùng nhóm nhu cầu và thương hiệu bạn hay xem";
            if (categoryScore >= 45m) return "Liên quan đến danh mục bạn đang quan tâm";
            if (brandScore >= 30m) return "Cùng thương hiệu bạn thường xem";
            if (keywordScore >= 20m) return "Khớp với từ khóa bạn từng tìm";
            if (IsInActiveFlashSale(product, activeFlashSale)) return "Đang có ưu đãi Flash Sale";
            if (HasDiscountPrice(product)) return "Đang có giá ưu đãi";
            return "Được nhiều khách quan tâm";
        }

        private static string NormalizeKey(string? key, string prefix)
        {
            if (!string.IsNullOrWhiteSpace(key) && key.Length <= 80) return key.Trim();
            return prefix + "_" + Guid.NewGuid().ToString("N");
        }

        private static string NormalizeEventName(string eventName)
        {
            return new string(eventName.Trim().ToLowerInvariant().Select(ch => char.IsLetterOrDigit(ch) || ch == '_' ? ch : '_').ToArray());
        }

        private static string? Safe(string? value, int maxLength)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            value = value.Trim();
            return value.Length <= maxLength ? value : value.Substring(0, maxLength);
        }

        private static string? HashIp(string? value)
        {
            if (string.IsNullOrWhiteSpace(value)) return null;
            using var sha = SHA256.Create();
            var bytes = sha.ComputeHash(Encoding.UTF8.GetBytes(value));
            return Convert.ToHexString(bytes).Substring(0, 32);
        }

        private static int TryReadInt(JsonElement? metadata, string propertyName)
        {
            try
            {
                if (metadata.HasValue && metadata.Value.ValueKind == JsonValueKind.Object && metadata.Value.TryGetProperty(propertyName, out var prop) && prop.TryGetInt32(out int value)) return value;
            }
            catch { }
            return 0;
        }

        private static string NormalizeSearchText(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;
            var text = input.Trim().ToLowerInvariant().Replace('đ', 'd').Replace('Đ', 'D');
            var normalized = text.Normalize(NormalizationForm.FormD);
            var builder = new StringBuilder();
            foreach (var c in normalized)
            {
                var category = CharUnicodeInfo.GetUnicodeCategory(c);
                if (category != UnicodeCategory.NonSpacingMark)
                {
                    builder.Append(char.IsLetterOrDigit(c) ? c : ' ');
                }
            }
            return string.Join(" ", builder.ToString().Normalize(NormalizationForm.FormC).Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static decimal GetDisplayPrice(ProductVariants variant)
        {
            return (variant.DiscountPrice.HasValue && variant.DiscountPrice.Value > 0) ? variant.DiscountPrice.Value : (variant.Price ?? 0);
        }

        private static ProductVariants? GetBestActiveVariant(Products product)
        {
            return product.ProductVariants?.Where(v => v.IsActive == true).OrderBy(v => GetDisplayPrice(v)).FirstOrDefault();
        }

        private static bool HasSellableStock(Products product)
        {
            return product.ProductVariants != null && product.ProductVariants.Any(v => v.IsActive == true && (v.Stock ?? 0) > 0);
        }

        private static bool HasDiscountPrice(Products product)
        {
            return product.ProductVariants != null && product.ProductVariants.Any(v => v.IsActive == true && v.DiscountPrice.HasValue && v.DiscountPrice.Value > 0 && v.Price.HasValue && v.DiscountPrice.Value < v.Price.Value);
        }

        private static bool IsInActiveFlashSale(Products product, FlashSales? activeFlashSale)
        {
            if (activeFlashSale?.FlashSaleItems == null || product.ProductVariants == null) return false;
            var variantIds = product.ProductVariants.Select(v => v.VariantId).ToHashSet();
            return activeFlashSale.FlashSaleItems.Any(item => variantIds.Contains(item.VariantId) && item.Quantity > item.Sold);
        }
    }
}
