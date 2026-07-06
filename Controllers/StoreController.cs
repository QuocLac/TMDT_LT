using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels.Storefront;
using TMDT_LT.Models.ViewModels;

namespace TMDT_LT.Controllers
{
    // Cấu trúc nhận dữ liệu Tracking ngầm từ Client gửi lên
    public class UserBehaviorTrackingDto
    {
        public int ProductId { get; set; }
        public int? TargetProductId { get; set; }
        public int ViewDuration { get; set; }
        public string? SearchKeyword { get; set; }
        public string ActionType { get; set; } = "ViewDuration";
    }

    public class StoreController : Controller
    {
        private readonly ApplicationDbContext _context;

        public StoreController(ApplicationDbContext context)
        {
            _context = context;
        }

        private static readonly Dictionary<string, string[]> SearchSynonyms = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
        {
            { "ip", new[] { "iphone", "apple" } },
            { "iphone", new[] { "ip", "apple" } },
            { "ss", new[] { "samsung" } },
            { "sam", new[] { "samsung" } },
            { "samsung", new[] { "ss" } },
            { "prm", new[] { "pro max", "promax" } },
            { "promax", new[] { "pro max" } },
            { "pro", new[] { "pro" } },
            { "max", new[] { "max" } },
            { "s24u", new[] { "s24 ultra", "samsung s24 ultra" } },
            { "s23u", new[] { "s23 ultra", "samsung s23 ultra" } },
            { "gaming", new[] { "choi game", "game", "snapdragon", "ram" } },
            { "game", new[] { "choi game", "gaming", "snapdragon", "ram" } },
            { "pin", new[] { "battery", "pin trau", "dung luong pin" } },
            { "camera", new[] { "chup anh", "may anh" } },
            { "chup", new[] { "camera", "chup anh" } },
            { "re", new[] { "gia re", "duoi 10 trieu", "duoi 15 trieu" } },
            { "titan", new[] { "titanium", "titan tu nhien", "titan xanh" } }
        };

        private static string NormalizeSearchText(string? input)
        {
            if (string.IsNullOrWhiteSpace(input)) return string.Empty;

            var normalized = StringHelper.RemoveDiacritics(input.Trim().ToLowerInvariant());
            var builder = new StringBuilder(normalized.Length);

            foreach (char ch in normalized)
            {
                builder.Append(char.IsLetterOrDigit(ch) ? ch : ' ');
            }

            return string.Join(" ", builder.ToString().Split(' ', StringSplitOptions.RemoveEmptyEntries));
        }

        private static List<string> ExpandSearchTokens(string? keyword)
        {
            var normalized = NormalizeSearchText(keyword);
            var tokens = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (!string.IsNullOrWhiteSpace(normalized))
            {
                tokens.Add(normalized);
                foreach (var part in normalized.Split(' ', StringSplitOptions.RemoveEmptyEntries))
                {
                    tokens.Add(part);
                    if (SearchSynonyms.TryGetValue(part, out var synonyms))
                    {
                        foreach (var synonym in synonyms)
                        {
                            tokens.Add(NormalizeSearchText(synonym));
                            foreach (var sub in NormalizeSearchText(synonym).Split(' ', StringSplitOptions.RemoveEmptyEntries))
                            {
                                tokens.Add(sub);
                            }
                        }
                    }
                }

                var compact = normalized.Replace(" ", "");
                if (!string.Equals(compact, normalized, StringComparison.OrdinalIgnoreCase))
                {
                    tokens.Add(compact);
                }

                if (compact.StartsWith("iphone") && compact.Length > 6)
                {
                    tokens.Add("iphone");
                }
                if (compact.StartsWith("ip") && compact.Length > 2)
                {
                    tokens.Add("iphone");
                }
            }

            return tokens.Where(x => !string.IsNullOrWhiteSpace(x)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        }

        private static string BuildSearchDocument(Products product)
        {
            var activeVariants = product.ProductVariants?.Where(v => v.IsActive == true) ?? Enumerable.Empty<ProductVariants>();
            var variantText = string.Join(" ", activeVariants.Select(v => $"{v.Color} {v.Ram} {v.Storage}"));

            return NormalizeSearchText($"{product.Name} {product.Brand?.BrandName} {product.Category?.CategoryName} {product.Chipset} {product.OperatingSystem} {product.ScreenTech} {product.RearCamera} {product.FrontCamera} {variantText}");
        }

        private static decimal GetDisplayPrice(ProductVariants variant)
        {
            return (variant.DiscountPrice.HasValue && variant.DiscountPrice.Value > 0)
                ? variant.DiscountPrice.Value
                : (variant.Price ?? 0);
        }

        private static ProductVariants? GetBestActiveVariant(Products product)
        {
            return product.ProductVariants?
                .Where(v => v.IsActive == true)
                .OrderBy(v => GetDisplayPrice(v))
                .FirstOrDefault();
        }

        private static decimal GetMinDisplayPrice(Products product)
        {
            var bestVariant = GetBestActiveVariant(product);
            return bestVariant == null ? 0 : GetDisplayPrice(bestVariant);
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

        private static int ComputeCommerceSearchScore(Products product, string? keyword, List<string> expandedTokens, FlashSales? activeFlashSale, List<int> preferredCategoryIds, List<int> preferredBrandIds)
        {
            if (string.IsNullOrWhiteSpace(keyword)) return 1;

            int score = 0;
            string normalizedKeyword = NormalizeSearchText(keyword);
            string name = NormalizeSearchText(product.Name);
            string brand = NormalizeSearchText(product.Brand?.BrandName);
            string category = NormalizeSearchText(product.Category?.CategoryName);
            string chipset = NormalizeSearchText(product.Chipset);
            string document = BuildSearchDocument(product);

            if (!string.IsNullOrWhiteSpace(name))
            {
                if (name == normalizedKeyword) score += 320;
                if (name.StartsWith(normalizedKeyword)) score += 260;
                if (name.Contains(normalizedKeyword)) score += 180;
            }

            if (!string.IsNullOrWhiteSpace(brand) && brand.Contains(normalizedKeyword)) score += 120;
            if (!string.IsNullOrWhiteSpace(category) && category.Contains(normalizedKeyword)) score += 80;
            if (!string.IsNullOrWhiteSpace(chipset) && chipset.Contains(normalizedKeyword)) score += 60;

            int matchedTokens = 0;
            foreach (var token in expandedTokens)
            {
                if (string.IsNullOrWhiteSpace(token)) continue;
                if (document.Contains(token))
                {
                    matchedTokens++;
                    score += token.Length >= 4 ? 18 : 8;
                }
            }

            if (expandedTokens.Count > 0 && matchedTokens == expandedTokens.Count) score += 90;
            else if (matchedTokens > 0) score += matchedTokens * 8;

            if (preferredCategoryIds.Contains(product.CategoryId)) score += 20;
            if (preferredBrandIds.Contains(product.BrandId)) score += 20;
            if (IsInActiveFlashSale(product, activeFlashSale)) score += 45;
            if (HasDiscountPrice(product)) score += 20;
            if (HasSellableStock(product)) score += 35;
            else score -= 200;

            return score;
        }

        private static string BuildProductUrl(Products product)
        {
            return $"/Store/Product/{product.ProductId}";
        }

        private static string FormatStockText(Products product)
        {
            int stock = product.ProductVariants?.Where(v => v.IsActive == true).Sum(v => v.Stock ?? 0) ?? 0;
            if (stock <= 0) return "Tạm hết hàng";
            if (stock <= 5) return $"Sắp hết: còn {stock}";
            return "Còn hàng";
        }

        // =======================================================
        // TRANG DANH SÁCH SẢN PHẨM & TÌM KIẾM (UNIFIED CATALOG ENGINE)
        // =======================================================
        [Route("Store")]
        [Route("Store/Index")]
        public async Task<IActionResult> Index(
                    string? keyword,
                    [FromQuery] List<int> brandIds,
                    [FromQuery] List<int> categoryIds,
                    decimal? minPrice,
                    decimal? maxPrice,
                    string sort = "newest",
                    int page = 1)
        {
            var now = DateTime.Now;

            // 1. KÉO DỮ LIỆU FLASH SALE ĐẨY RA GIAO DIỆN CATALOG
            var activeFlashSale = await _context.FlashSales
                .Include(f => f.FlashSaleItems)
                .Where(f => f.IsActive && f.StartTime <= now && f.EndTime >= now)
                .FirstOrDefaultAsync();
            ViewBag.ActiveFlashSale = activeFlashSale;

            int currentCustomerId = 0;
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                string userIdStr = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0";
                int.TryParse(userIdStr, out currentCustomerId);
            }

            List<int> preferredCategoryIds = new List<int>();
            List<int> preferredBrandIds = new List<int>();

            if (currentCustomerId > 0)
            {
                var userInteractedProductIds = await _context.UserBehaviorLogs
                    .Where(log => log.CustomerId == currentCustomerId && (log.ViewDuration > 5 || log.ActionType == "ProductClick"))
                    .Select(log => log.ProductId)
                    .Distinct()
                    .Take(50)
                    .ToListAsync();

                if (userInteractedProductIds.Any())
                {
                    var productDetails = await _context.Products
                        .Where(p => userInteractedProductIds.Contains(p.ProductId))
                        .Select(p => new { p.CategoryId, p.BrandId })
                        .ToListAsync();

                    preferredCategoryIds = productDetails.Select(x => x.CategoryId).Distinct().ToList();
                    preferredBrandIds = productDetails.Select(x => x.BrandId).Distinct().ToList();
                }
            }

            var rawProducts = await _context.Products
                .Include(p => p.Category)
                .Include(p => p.Brand)
                .Include(p => p.ProductVariants)
                .Where(p => p.IsActive == true)
                .ToListAsync();

            var scoredResults = new List<SearchResultItemVM>();
            var expandedSearchTokens = ExpandSearchTokens(keyword);

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                scoredResults = rawProducts
                    .Select(p => new SearchResultItemVM
                    {
                        Product = p,
                        RelevanceScore = ComputeCommerceSearchScore(p, keyword, expandedSearchTokens, activeFlashSale, preferredCategoryIds, preferredBrandIds)
                    })
                    .Where(x => x.RelevanceScore > 0)
                    .ToList();
            }
            else
            {
                scoredResults = rawProducts.Select(p =>
                {
                    int score = 1;
                    if (preferredCategoryIds.Contains(p.CategoryId)) score += 15;
                    if (preferredBrandIds.Contains(p.BrandId)) score += 15;
                    if (IsInActiveFlashSale(p, activeFlashSale)) score += 40;
                    if (HasDiscountPrice(p)) score += 15;
                    if (HasSellableStock(p)) score += 20;
                    else score -= 100;

                    return new SearchResultItemVM { Product = p, RelevanceScore = score };
                }).ToList();
            }

            if (brandIds != null && brandIds.Any())
            {
                scoredResults = scoredResults.Where(x => brandIds.Contains(x.Product.BrandId)).ToList();
            }

            if (categoryIds != null && categoryIds.Any())
            {
                scoredResults = scoredResults.Where(x => categoryIds.Contains(x.Product.CategoryId)).ToList();
            }

            if (minPrice.HasValue || maxPrice.HasValue)
            {
                scoredResults = scoredResults.Where(x =>
                {
                    var activeVariants = x.Product.ProductVariants.Where(v => v.IsActive == true);
                    if (!activeVariants.Any()) return false;

                    var minVariantPrice = GetMinDisplayPrice(x.Product);
                    bool matchMin = !minPrice.HasValue || minVariantPrice >= minPrice.Value;
                    bool matchMax = !maxPrice.HasValue || minVariantPrice <= maxPrice.Value;

                    return matchMin && matchMax;
                }).ToList();
            }

            if (!string.IsNullOrWhiteSpace(keyword))
            {
                scoredResults = scoredResults
                    .OrderByDescending(x => x.RelevanceScore)
                    .ThenByDescending(x => HasSellableStock(x.Product))
                    .ThenByDescending(x => IsInActiveFlashSale(x.Product, activeFlashSale))
                    .ThenByDescending(x => x.Product.CreatedDate)
                    .ToList();
            }
            else
            {
                switch (sort)
                {
                    case "price_asc":
                        scoredResults = scoredResults.OrderBy(x => GetMinDisplayPrice(x.Product)).ToList();
                        break;
                    case "price_desc":
                        scoredResults = scoredResults.OrderByDescending(x => GetMinDisplayPrice(x.Product)).ToList();
                        break;
                    case "bestseller-desc":
                        scoredResults = scoredResults.OrderByDescending(x => x.Product.ProductVariants.SelectMany(v => _context.OrderDetails.Where(od => od.VariantId == v.VariantId)).Sum(od => (int?)od.Quantity) ?? 0).ToList();
                        break;
                    default:
                        scoredResults = scoredResults.OrderByDescending(x => x.RelevanceScore).ThenByDescending(x => x.Product.CreatedDate).ToList();
                        break;
                }
            }

            int pageSize = 12;
            int totalItems = scoredResults.Count;
            int totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

            if (page < 1) page = 1;
            if (page > totalPages && totalPages > 0) page = totalPages;

            var pagedResults = scoredResults.Skip((page - 1) * pageSize).Take(pageSize).ToList();

            var dbBrands = await _context.Brands.Select(b => b.BrandName).Take(5).ToListAsync();
            var dbCategories = await _context.Categories.Where(c => c.IsActive == true).Select(c => c.CategoryName).Take(4).ToListAsync();

            var popularSearches = await _context.UserBehaviorLogs
                .Where(log => !string.IsNullOrEmpty(log.SearchKeyword))
                .GroupBy(log => log.SearchKeyword)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key)
                .Take(3)
                .ToListAsync();

            var tags = new List<string>();
            if (popularSearches.Any()) tags.AddRange(popularSearches!);
            tags.AddRange(dbBrands);
            tags.AddRange(dbCategories);

            if (!tags.Any())
            {
                tags = new List<string> { "iPhone", "Samsung", "Oppo", "Laptop", "iPad" };
            }

            var vm = new CatalogVM
            {
                Keyword = keyword,
                AvailableBrands = await _context.Brands.ToListAsync(),
                AvailableCategories = await _context.Categories.ToListAsync(),
                Products = pagedResults,
                TotalResults = totalItems,
                SelectedBrandIds = brandIds ?? new List<int>(),
                SelectedCategoryIds = categoryIds ?? new List<int>(),
                MinPrice = minPrice,
                MaxPrice = maxPrice,
                SortBy = sort,
                CurrentPage = page,
                TotalPages = totalPages,
                PageSize = pageSize,
                SuggestionTags = tags.Distinct().ToList()
            };

            return View(vm);
        }

        // =====================================================================
        // API GỢI Ý TÌM KIẾM THƯƠNG MẠI: TỪ KHÓA + SẢN PHẨM + THƯƠNG HIỆU/DANH MỤC
        // =====================================================================
        [HttpGet]
        [Route("Store/GetSearchSuggestions")]
        public async Task<IActionResult> GetSearchSuggestions(string q)
        {
            var now = DateTime.Now;
            string normalizedQuery = NormalizeSearchText(q);
            var expandedTokens = ExpandSearchTokens(q);

            var activeFlashSale = await _context.FlashSales
                .Include(f => f.FlashSaleItems)
                .Where(f => f.IsActive && f.StartTime <= now && f.EndTime >= now)
                .FirstOrDefaultAsync();

            int currentCustomerId = 0;
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                string userIdStr = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0";
                int.TryParse(userIdStr, out currentCustomerId);
            }

            List<int> preferredCategoryIds = new List<int>();
            List<int> preferredBrandIds = new List<int>();
            if (currentCustomerId > 0)
            {
                var userInteractedProductIds = await _context.UserBehaviorLogs
                    .Where(log => log.CustomerId == currentCustomerId && (log.ViewDuration > 5 || log.ActionType == "ProductClick"))
                    .Select(log => log.ProductId)
                    .Distinct()
                    .Take(30)
                    .ToListAsync();

                if (userInteractedProductIds.Any())
                {
                    var profileHints = await _context.Products
                        .Where(p => userInteractedProductIds.Contains(p.ProductId))
                        .Select(p => new { p.CategoryId, p.BrandId })
                        .ToListAsync();

                    preferredCategoryIds = profileHints.Select(x => x.CategoryId).Distinct().ToList();
                    preferredBrandIds = profileHints.Select(x => x.BrandId).Distinct().ToList();
                }
            }

            var hotKeywords = await _context.UserBehaviorLogs
                .Where(log => !string.IsNullOrEmpty(log.SearchKeyword))
                .GroupBy(log => log.SearchKeyword)
                .OrderByDescending(g => g.Count())
                .Select(g => g.Key!)
                .Take(8)
                .ToListAsync();

            if (!hotKeywords.Any())
            {
                hotKeywords = new List<string> { "iPhone 15 Pro Max", "Samsung S24 Ultra", "điện thoại dưới 10 triệu", "máy chơi game", "pin trâu", "camera đẹp" };
            }

            var products = await _context.Products
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .Include(p => p.ProductVariants)
                .Where(p => p.IsActive == true)
                .ToListAsync();

            var brands = await _context.Brands.ToListAsync();
            var categories = await _context.Categories.Where(c => c.IsActive == true).ToListAsync();

            var keywordSuggestions = new List<string>();
            var taxonomySuggestions = new List<object>();

            if (string.IsNullOrWhiteSpace(q))
            {
                keywordSuggestions.AddRange(hotKeywords);

                taxonomySuggestions.AddRange(brands.Take(4).Select(b => new
                {
                    label = b.BrandName,
                    subLabel = "Thương hiệu",
                    url = $"/Store?brandIds={b.BrandId}",
                    icon = "fa-tag"
                }));
            }
            else
            {
                keywordSuggestions.AddRange(hotKeywords.Where(k => NormalizeSearchText(k).Contains(normalizedQuery)).Take(3));

                var matchedBrands = brands
                    .Where(b => NormalizeSearchText(b.BrandName).Contains(normalizedQuery) || expandedTokens.Any(t => NormalizeSearchText(b.BrandName).Contains(t)))
                    .Take(3)
                    .ToList();

                var matchedCategories = categories
                    .Where(c => NormalizeSearchText(c.CategoryName).Contains(normalizedQuery) || expandedTokens.Any(t => NormalizeSearchText(c.CategoryName).Contains(t)))
                    .Take(3)
                    .ToList();

                taxonomySuggestions.AddRange(matchedBrands.Select(b => new
                {
                    label = b.BrandName,
                    subLabel = "Thương hiệu",
                    url = $"/Store?brandIds={b.BrandId}&keyword={Uri.EscapeDataString(q ?? string.Empty)}",
                    icon = "fa-tag"
                }));

                taxonomySuggestions.AddRange(matchedCategories.Select(c => new
                {
                    label = c.CategoryName,
                    subLabel = "Danh mục",
                    url = $"/Store?categoryIds={c.CategoryId}&keyword={Uri.EscapeDataString(q ?? string.Empty)}",
                    icon = "fa-layer-group"
                }));

                keywordSuggestions.AddRange(products
                    .Where(p => ComputeCommerceSearchScore(p, q, expandedTokens, activeFlashSale, preferredCategoryIds, preferredBrandIds) > 0)
                    .OrderByDescending(p => ComputeCommerceSearchScore(p, q, expandedTokens, activeFlashSale, preferredCategoryIds, preferredBrandIds))
                    .Select(p => p.Name)
                    .Take(5));

                foreach (var token in expandedTokens.Where(t => t.Length >= 3).Take(4))
                {
                    keywordSuggestions.Add(token);
                }
            }

            var productSuggestions = products
                .Select(p => new
                {
                    Product = p,
                    Score = string.IsNullOrWhiteSpace(q)
                        ? (IsInActiveFlashSale(p, activeFlashSale) ? 80 : 0) + (HasSellableStock(p) ? 30 : -100) + (HasDiscountPrice(p) ? 20 : 0) + (preferredBrandIds.Contains(p.BrandId) ? 20 : 0) + (preferredCategoryIds.Contains(p.CategoryId) ? 20 : 0)
                        : ComputeCommerceSearchScore(p, q, expandedTokens, activeFlashSale, preferredCategoryIds, preferredBrandIds)
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => HasSellableStock(x.Product))
                .ThenByDescending(x => IsInActiveFlashSale(x.Product, activeFlashSale))
                .Take(5)
                .Select(x =>
                {
                    var bestVariant = GetBestActiveVariant(x.Product);
                    decimal price = bestVariant == null ? 0 : GetDisplayPrice(bestVariant);
                    decimal? oldPrice = bestVariant?.Price;
                    bool hasSale = IsInActiveFlashSale(x.Product, activeFlashSale) || HasDiscountPrice(x.Product);

                    return new
                    {
                        productId = x.Product.ProductId,
                        name = x.Product.Name,
                        brand = x.Product.Brand?.BrandName ?? "",
                        category = x.Product.Category?.CategoryName ?? "",
                        imageUrl = !string.IsNullOrWhiteSpace(bestVariant?.ImageUrl) ? bestVariant.ImageUrl : (x.Product.MainImage ?? "/images/placeholder-product.png"),
                        price,
                        oldPrice = oldPrice.HasValue && oldPrice.Value > price ? oldPrice : null,
                        url = BuildProductUrl(x.Product),
                        stockText = FormatStockText(x.Product),
                        isFlashSale = IsInActiveFlashSale(x.Product, activeFlashSale),
                        hasSale,
                        score = x.Score
                    };
                })
                .ToList();

            var finalKeywords = keywordSuggestions
                .Where(k => !string.IsNullOrWhiteSpace(k))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(8)
                .Select(k => new
                {
                    label = k,
                    url = $"/Store?keyword={Uri.EscapeDataString(k)}"
                })
                .ToList();

            return Json(new
            {
                isBlank = string.IsNullOrWhiteSpace(q),
                normalizedQuery,
                phrases = finalKeywords.Select(k => k.label).ToList(),
                sections = new
                {
                    keywords = finalKeywords,
                    products = productSuggestions,
                    taxonomy = taxonomySuggestions.Take(6).ToList()
                }
            });
        }

        [HttpPost]
        [Route("Store/TrackUserBehavior")]
        public async Task<IActionResult> TrackUserBehavior([FromBody] UserBehaviorTrackingDto dto)
        {
            if (dto == null) return BadRequest();

            int? customerId = null;
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                string userIdStr = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0";
                if (int.TryParse(userIdStr, out int id) && id > 0)
                {
                    customerId = id;
                }
            }

            var logEntry = new UserBehaviorLog
            {
                CustomerId = customerId,
                ProductId = dto.ProductId,
                TargetProductId = dto.TargetProductId,
                ViewDuration = dto.ViewDuration,
                SearchKeyword = string.IsNullOrWhiteSpace(dto.SearchKeyword) ? null : dto.SearchKeyword.Trim(),
                ActionType = dto.ActionType,
                CreatedAt = DateTime.UtcNow
            };

            _context.UserBehaviorLogs.Add(logEntry);
            await _context.SaveChangesAsync();

            return Json(new { success = true });
        }


        private int? GetCurrentCustomerId()
        {
            if (User.Identity?.IsAuthenticated != true) return null;
            var raw = User.FindFirst("CustomerId")?.Value;
            return int.TryParse(raw, out int customerId) && customerId > 0 ? customerId : null;
        }

        private static void AddRecommendationSignal(Dictionary<int, int> signals, int? productId, int weight)
        {
            if (!productId.HasValue || productId.Value <= 0) return;
            signals[productId.Value] = signals.TryGetValue(productId.Value, out int current) ? current + weight : weight;
        }

        private async Task<List<Products>> BuildBehaviorBasedRecommendationsAsync(Products currentProduct, FlashSales? activeFlashSale, int take = 12)
        {
            take = Math.Clamp(take, 6, 18);
            int? customerId = GetCurrentCustomerId();
            var now = DateTime.Now;
            var sinceUtc = DateTime.UtcNow.AddDays(-30);
            var behaviorSignals = new Dictionary<int, int>();

            if (customerId.HasValue)
            {
                var behaviorLogs = await _context.UserBehaviorLogs
                    .Where(x => x.CustomerId == customerId.Value)
                    .OrderByDescending(x => x.CreatedAt)
                    .Take(120)
                    .Select(x => new { x.ProductId, x.TargetProductId, x.ViewDuration, x.ActionType })
                    .ToListAsync();

                foreach (var log in behaviorLogs)
                {
                    int baseWeight = log.ActionType == "ProductClick" ? 24 : Math.Clamp(log.ViewDuration / 4, 4, 20);
                    AddRecommendationSignal(behaviorSignals, log.ProductId, baseWeight);
                    AddRecommendationSignal(behaviorSignals, log.TargetProductId, 28);
                }

                var analyticsEvents = await _context.AnalyticsEvents
                    .Where(e => e.CustomerId == customerId.Value && e.CreatedAt >= sinceUtc)
                    .OrderByDescending(e => e.CreatedAt)
                    .Take(180)
                    .Select(e => new { e.EventName, e.ProductId, e.TargetProductId, e.Quantity })
                    .ToListAsync();

                foreach (var e in analyticsEvents)
                {
                    int weight = e.EventName switch
                    {
                        "purchase" => 70,
                        "begin_checkout" => 45,
                        "add_to_cart" => 38,
                        "view_item" => 14,
                        _ => 8
                    };
                    AddRecommendationSignal(behaviorSignals, e.ProductId, weight);
                    AddRecommendationSignal(behaviorSignals, e.TargetProductId, weight + 8);
                }
            }

            var viewStats = await _context.AnalyticsEvents
                .Where(e => e.EventName == "view_item" && e.ProductId.HasValue && e.CreatedAt >= sinceUtc)
                .GroupBy(e => e.ProductId!.Value)
                .Select(g => new { ProductId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ProductId, x => x.Count);

            var cartStats = await _context.AnalyticsEvents
                .Where(e => e.EventName == "add_to_cart" && e.ProductId.HasValue && e.CreatedAt >= sinceUtc)
                .GroupBy(e => e.ProductId!.Value)
                .Select(g => new { ProductId = g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.ProductId, x => x.Count);

            var soldStats = await _context.OrderDetails
                .Where(d => d.Order != null
                    && d.Order.OrderDate >= now.AddDays(-45)
                    && d.Order.Status != "Đã hủy"
                    && d.Order.Status != "Đã hoàn trả"
                    && d.Variant != null)
                .GroupBy(d => d.Variant!.ProductId)
                .Select(g => new { ProductId = g.Key, Quantity = g.Sum(x => x.Quantity ?? 0) })
                .ToDictionaryAsync(x => x.ProductId, x => x.Quantity);

            var signalProductIds = behaviorSignals.Keys.Append(currentProduct.ProductId).Distinct().Take(80).ToList();

            var preferenceHints = await _context.Products
                .Where(p => signalProductIds.Contains(p.ProductId))
                .Select(p => new { p.BrandId, p.CategoryId })
                .ToListAsync();

            var preferredBrands = preferenceHints.Select(x => x.BrandId).Distinct().ToHashSet();
            var preferredCategories = preferenceHints.Select(x => x.CategoryId).Distinct().ToHashSet();
            decimal currentMinPrice = GetMinDisplayPrice(currentProduct);

            var candidates = await _context.Products
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .Include(p => p.ProductVariants.Where(v => v.IsActive == true))
                .Where(p => p.IsActive == true && p.ProductId != currentProduct.ProductId)
                .ToListAsync();

            var ranked = candidates
                .Where(p => p.ProductVariants.Any(v => v.IsActive == true))
                .Select(p =>
                {
                    int score = 0;
                    if (p.CategoryId == currentProduct.CategoryId) score += 70;
                    if (p.BrandId == currentProduct.BrandId) score += 45;
                    if (preferredCategories.Contains(p.CategoryId)) score += 42;
                    if (preferredBrands.Contains(p.BrandId)) score += 34;
                    if (behaviorSignals.TryGetValue(p.ProductId, out int signalScore)) score += Math.Min(signalScore, 90);
                    if (viewStats.TryGetValue(p.ProductId, out int views)) score += Math.Min(views * 2, 50);
                    if (cartStats.TryGetValue(p.ProductId, out int carts)) score += Math.Min(carts * 8, 64);
                    if (soldStats.TryGetValue(p.ProductId, out int sold)) score += Math.Min(sold * 5, 80);
                    if (IsInActiveFlashSale(p, activeFlashSale)) score += 48;
                    if (HasDiscountPrice(p)) score += 24;
                    if (HasSellableStock(p)) score += 36; else score -= 250;

                    decimal candidatePrice = GetMinDisplayPrice(p);
                    if (currentMinPrice > 0 && candidatePrice > 0)
                    {
                        decimal diffRate = Math.Abs(candidatePrice - currentMinPrice) / currentMinPrice;
                        if (diffRate <= 0.15m) score += 26;
                        else if (diffRate <= 0.35m) score += 14;
                    }

                    if (behaviorSignals.ContainsKey(p.ProductId)) score -= 10;
                    return new { Product = p, Score = score };
                })
                .Where(x => x.Score > 0)
                .OrderByDescending(x => x.Score)
                .ThenByDescending(x => HasSellableStock(x.Product))
                .ThenByDescending(x => IsInActiveFlashSale(x.Product, activeFlashSale))
                .ThenByDescending(x => x.Product.CreatedDate)
                .Take(take)
                .Select(x => x.Product)
                .ToList();

            return ranked;
        }

        [Route("Store/Product/{id}")]
        public async Task<IActionResult> Product(int id)
        {
            var now = DateTime.Now;

            // ====================================================================
            // 1. LẤY THÔNG TIN FLASH SALE ĐỂ TRUYỀN SANG GIAO DIỆN (ĐÃ BỔ SUNG)
            // ====================================================================
            var activeFlashSale = await _context.FlashSales
                .Include(f => f.FlashSaleItems)
                .Where(f => f.IsActive && f.StartTime <= now && f.EndTime >= now)
                .FirstOrDefaultAsync();
            ViewBag.ActiveFlashSale = activeFlashSale;

            // ====================================================================
            // 2. LẤY THÔNG TIN SẢN PHẨM CHÍNH
            // ====================================================================
            var product = await _context.Products
                .Include(p => p.Brand)
                .Include(p => p.Category)
                .Include(p => p.ProductVariants.Where(v => v.IsActive == true))
                .Include(p => p.ProductImages)
                .FirstOrDefaultAsync(p => p.ProductId == id && p.IsActive == true);

            if (product == null) return RedirectToAction("Index");

            // ====================================================================
            // 3. GỢI Ý SẢN PHẨM THEO MỨC ĐỘ LIÊN QUAN THƯƠNG MẠI
            //    Ưu tiên: cùng nhu cầu, hành vi tài khoản, lượt xem/thêm giỏ/mua, ưu đãi, còn hàng.
            // ====================================================================
            var upSellProducts = await BuildBehaviorBasedRecommendationsAsync(product, activeFlashSale, 12);

            // ====================================================================
            // 4. LẤY ĐÁNH GIÁ (REVIEWS)
            // ====================================================================
            var allReviews = await _context.Reviews
                .Include(r => r.Customer)
                .Include(r => r.ReviewDetails).ThenInclude(rd => rd.Variant)
                .Where(r => r.ProductId == id && r.IsHidden == false)
                .OrderByDescending(r => r.CreatedAt)
                .ToListAsync();

            double avgRating = allReviews.Any() ? (double)allReviews.Average(r => r.Rating ?? 0) : 0;
            var starCounts = new Dictionary<int, int> { { 5, 0 }, { 4, 0 }, { 3, 0 }, { 2, 0 }, { 1, 0 } };
            foreach (var r in allReviews)
            {
                int star = r.Rating ?? 5;
                if (starCounts.ContainsKey(star)) starCounts[star]++;
            }

            var displayReviews = allReviews.Take(3).ToList();

            var model = new ProductDetailVM
            {
                Product = product,
                UpSellProducts = upSellProducts,
                ApprovedReviews = displayReviews,
                AverageRating = avgRating,
                TotalReviews = allReviews.Count,
                StarCounts = starCounts
            };

            // ====================================================================
            // 5. KIỂM TRA LỊCH SỬ MUA HÀNG CỦA USER
            // ====================================================================
            bool hasPurchased = false;
            if (User.Identity != null && User.Identity.IsAuthenticated)
            {
                string userIdStr = User.FindFirst("CustomerId")?.Value ?? User.FindFirst(System.Security.Claims.ClaimTypes.NameIdentifier)?.Value ?? "0";
                if (int.TryParse(userIdStr, out int customerId))
                {
                    hasPurchased = await _context.Orders
                        .Include(o => o.OrderDetails)
                        .AnyAsync(o => o.CustomerId == customerId && o.Status == "Đã hoàn thành" && o.OrderDetails.Any(d => d.Variant.ProductId == id));
                }
            }
            ViewBag.HasPurchased = hasPurchased;

            return View(model);
        }

        [Route("Store/Product/{id}/Reviews")]
        public async Task<IActionResult> ProductReviews(int id, int? starFilter)
        {
            var product = await _context.Products.FirstOrDefaultAsync(p => p.ProductId == id);
            if (product == null) return NotFound();

            var query = _context.Reviews
                .Include(r => r.Customer)
                .Include(r => r.ReviewDetails).ThenInclude(rd => rd.Variant)
                .Where(r => r.ProductId == id && r.IsHidden == false);

            var allReviews = await query.ToListAsync();

            double avgRating = allReviews.Any() ? (double)allReviews.Average(r => r.Rating ?? 0) : 0;
            var starCounts = new Dictionary<int, int> { { 5, 0 }, { 4, 0 }, { 3, 0 }, { 2, 0 }, { 1, 0 } };

            foreach (var r in allReviews)
            {
                int star = r.Rating ?? 5;
                if (starCounts.ContainsKey(star)) starCounts[star]++;
            }

            if (starFilter.HasValue && starFilter.Value >= 1 && starFilter.Value <= 5)
            {
                query = query.Where(r => r.Rating == starFilter.Value);
            }

            var displayReviews = await query.OrderByDescending(r => r.CreatedAt).ToListAsync();

            ViewBag.Product = product;
            ViewBag.AverageRating = avgRating;
            ViewBag.TotalReviews = allReviews.Count;
            ViewBag.StarCounts = starCounts;
            ViewBag.SelectedStar = starFilter;

            return View(displayReviews);
        }
    }

    public static class StringHelper
    {
        public static string RemoveDiacritics(string text)
        {
            if (string.IsNullOrWhiteSpace(text)) return text;

            text = text.Replace('đ', 'd').Replace('Đ', 'D');

            var normalizedString = text.Normalize(NormalizationForm.FormD);
            var stringBuilder = new StringBuilder(capacity: normalizedString.Length);

            for (int i = 0; i < normalizedString.Length; i++)
            {
                char c = normalizedString[i];
                var unicodeCategory = CharUnicodeInfo.GetUnicodeCategory(c);
                if (unicodeCategory != UnicodeCategory.NonSpacingMark)
                {
                    stringBuilder.Append(c);
                }
            }

            return stringBuilder.ToString().Normalize(NormalizationForm.FormC);
        }
    }
}