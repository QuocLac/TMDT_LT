using System.Globalization;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.AI;
using TMDT_LT.Services;

namespace TMDT_LT.Services.AI;

public sealed class KingPhoneAiToolExecutionResult
{
    public string ToolName { get; init; } = string.Empty;
    public string OutputJson { get; init; } = "{}";
    public List<KingPhoneAiProductCard> Products { get; init; } = new();
    public List<string> QuickReplies { get; init; } = new();
}

public interface IKingPhoneAiToolService
{
    IReadOnlyList<object> GetToolDefinitions();

    Task<KingPhoneAiToolExecutionResult> ExecuteAsync(
        string toolName,
        JsonElement arguments,
        int? customerId,
        CancellationToken cancellationToken = default);
}

public sealed class KingPhoneAiToolService : IKingPhoneAiToolService
{
    private static readonly JsonSerializerOptions ToolJsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private static readonly string[] RevenueOrderStatuses =
    {
        "Hoàn thành",
        "Đã giao",
        "Đã nhận hàng"
    };

    private readonly ApplicationDbContext _context;
    private readonly PromotionEngine _promotionEngine;
    private readonly ICrossSellAprioriService _crossSellAprioriService;
    private readonly ILogger<KingPhoneAiToolService> _logger;

    public KingPhoneAiToolService(
        ApplicationDbContext context,
        PromotionEngine promotionEngine,
        ICrossSellAprioriService crossSellAprioriService,
        ILogger<KingPhoneAiToolService> logger)
    {
        _context = context;
        _promotionEngine = promotionEngine;
        _crossSellAprioriService = crossSellAprioriService;
        _logger = logger;
    }

    public IReadOnlyList<object> GetToolDefinitions()
    {
        return new object[]
        {
            new
            {
                type = "function",
                name = "search_products",
                description = "Tìm sản phẩm KingPhone theo từ khóa, nhu cầu, thương hiệu, danh mục, khoảng giá và tình trạng tồn kho. Phải dùng tool này trước khi xác nhận giá hoặc tồn kho.",
                strict = true,
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["keyword"] = NullableString("Tên, dòng máy hoặc từ khóa khách đang tìm."),
                        ["need"] = NullableString("Nhu cầu sử dụng như chơi game, chụp ảnh, pin tốt, học tập hoặc công việc."),
                        ["brand"] = NullableString("Tên thương hiệu; null nếu khách không yêu cầu."),
                        ["category"] = NullableString("Tên danh mục; null nếu khách không yêu cầu."),
                        ["min_price"] = NullableNumber("Giá tối thiểu bằng VND; null nếu không giới hạn."),
                        ["max_price"] = NullableNumber("Giá tối đa bằng VND; null nếu không giới hạn."),
                        ["in_stock_only"] = new
                        {
                            type = "boolean",
                            description = "true khi chỉ lấy sản phẩm đang còn hàng."
                        },
                        ["limit"] = new
                        {
                            type = "integer",
                            minimum = 1,
                            maximum = 8,
                            description = "Số sản phẩm tối đa cần trả về."
                        }
                    },
                    required = new[]
                    {
                        "keyword", "need", "brand", "category", "min_price", "max_price", "in_stock_only", "limit"
                    },
                    additionalProperties = false
                }
            },
            new
            {
                type = "function",
                name = "get_product_details",
                description = "Lấy thông số, biến thể, giá và tồn kho trực tiếp của một sản phẩm KingPhone.",
                strict = true,
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["product_id"] = NullableInteger("Mã sản phẩm nếu đã biết; null nếu tìm theo tên."),
                        ["product_name"] = NullableString("Tên sản phẩm nếu chưa biết mã; null khi đã có product_id.")
                    },
                    required = new[] { "product_id", "product_name" },
                    additionalProperties = false
                }
            },
            new
            {
                type = "function",
                name = "compare_products",
                description = "So sánh tối đa bốn sản phẩm KingPhone bằng dữ liệu trực tiếp về giá, tồn kho và thông số.",
                strict = true,
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["product_ids"] = new
                        {
                            type = "array",
                            items = new { type = "integer" },
                            maxItems = 4,
                            description = "Danh sách mã sản phẩm đã biết; có thể để mảng rỗng."
                        },
                        ["product_names"] = new
                        {
                            type = "array",
                            items = new { type = "string" },
                            maxItems = 4,
                            description = "Danh sách tên sản phẩm; có thể để mảng rỗng."
                        }
                    },
                    required = new[] { "product_ids", "product_names" },
                    additionalProperties = false
                }
            },
            new
            {
                type = "function",
                name = "get_current_promotions",
                description = "Kiểm tra voucher và Flash Sale đang hoạt động tại KingPhone. Kết quả là ước tính; checkout vẫn xác thực lại lần cuối.",
                strict = true,
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["cart_value"] = NullableNumber("Tạm tính giỏ hàng bằng VND; null nếu khách chưa cung cấp."),
                        ["product_id"] = NullableInteger("Mã sản phẩm cần kiểm tra ưu đãi; null nếu hỏi chung."),
                        ["limit"] = new
                        {
                            type = "integer",
                            minimum = 1,
                            maximum = 8,
                            description = "Số ưu đãi tối đa cần trả về."
                        }
                    },
                    required = new[] { "cart_value", "product_id", "limit" },
                    additionalProperties = false
                }
            },
            new
            {
                type = "function",
                name = "get_personalized_recommendations",
                description = "Lấy gợi ý sản phẩm từ hành vi của tài khoản hiện tại, thuật toán bán kèm Apriori và dữ liệu bán hàng. Nếu chưa đăng nhập, trả gợi ý phổ biến thay vì giả vờ cá nhân hóa.",
                strict = true,
                parameters = new
                {
                    type = "object",
                    properties = new Dictionary<string, object>
                    {
                        ["need"] = NullableString("Nhu cầu khách vừa nêu; null nếu chưa có."),
                        ["max_price"] = NullableNumber("Ngân sách tối đa bằng VND; null nếu chưa có."),
                        ["limit"] = new
                        {
                            type = "integer",
                            minimum = 1,
                            maximum = 8,
                            description = "Số sản phẩm tối đa cần trả về."
                        }
                    },
                    required = new[] { "need", "max_price", "limit" },
                    additionalProperties = false
                }
            }
        };
    }

    public async Task<KingPhoneAiToolExecutionResult> ExecuteAsync(
        string toolName,
        JsonElement arguments,
        int? customerId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            return toolName switch
            {
                "search_products" => await SearchProductsAsync(arguments, cancellationToken),
                "get_product_details" => await GetProductDetailsAsync(arguments, cancellationToken),
                "compare_products" => await CompareProductsAsync(arguments, cancellationToken),
                "get_current_promotions" => await GetCurrentPromotionsAsync(arguments, customerId, cancellationToken),
                "get_personalized_recommendations" => await GetPersonalizedRecommendationsAsync(arguments, customerId, cancellationToken),
                _ => Error(toolName, "UNKNOWN_TOOL", "Tool không nằm trong danh sách được KingPhone cho phép.")
            };
        }
        catch (JsonException exception)
        {
            _logger.LogWarning(exception, "KingPhone AI tool {ToolName} received invalid arguments.", toolName);
            return Error(toolName, "INVALID_ARGUMENTS", "Dữ liệu gọi tool không hợp lệ.");
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception exception)
        {
            _logger.LogError(exception, "KingPhone AI tool {ToolName} failed.", toolName);
            return Error(toolName, "TOOL_EXECUTION_FAILED", "KingPhone chưa thể truy xuất dữ liệu này ở thời điểm hiện tại.");
        }
    }

    private async Task<KingPhoneAiToolExecutionResult> SearchProductsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var keyword = ReadNullableString(arguments, "keyword");
        var need = ReadNullableString(arguments, "need");
        var brand = ReadNullableString(arguments, "brand");
        var category = ReadNullableString(arguments, "category");
        var minPrice = ReadNullableDecimal(arguments, "min_price");
        var maxPrice = ReadNullableDecimal(arguments, "max_price");
        var inStockOnly = ReadBoolean(arguments, "in_stock_only", true);
        var limit = Math.Clamp(ReadInteger(arguments, "limit", 5), 1, 8);

        if (minPrice.HasValue && maxPrice.HasValue && minPrice > maxPrice)
        {
            (minPrice, maxPrice) = (maxPrice, minPrice);
        }

        var products = await LoadActiveCatalogAsync(cancellationToken);
        var flashByVariant = await LoadActiveFlashPricesAsync(cancellationToken);

        var normalizedBrand = Normalize(brand);
        var normalizedCategory = Normalize(category);
        var candidates = new List<RankedProduct>();

        foreach (var product in products)
        {
            if (!string.IsNullOrWhiteSpace(normalizedBrand)
                && !Normalize(product.Brand?.BrandName).Contains(normalizedBrand))
            {
                continue;
            }

            if (!string.IsNullOrWhiteSpace(normalizedCategory)
                && !Normalize(product.Category?.CategoryName).Contains(normalizedCategory))
            {
                continue;
            }

            var card = BuildBestProductCard(product, flashByVariant, inStockOnly, minPrice, maxPrice);
            if (card == null)
            {
                continue;
            }

            var score = ComputeProductScore(product, card, keyword, need);
            if ((!string.IsNullOrWhiteSpace(keyword) || !string.IsNullOrWhiteSpace(need)) && score <= 0)
            {
                continue;
            }

            candidates.Add(new RankedProduct(product, card, score));
        }

        var selected = candidates
            .OrderByDescending(item => item.Score)
            .ThenByDescending(item => item.Card.IsFlashSale)
            .ThenBy(item => item.Card.Price)
            .ThenByDescending(item => item.Card.Stock)
            .Take(limit)
            .Select(item => item.Card)
            .ToList();

        return Success(
            "search_products",
            new
            {
                query = new
                {
                    keyword,
                    need,
                    brand,
                    category,
                    minPrice,
                    maxPrice,
                    inStockOnly
                },
                count = selected.Count,
                items = selected,
                dataFreshAt = DateTime.Now,
                note = "Giá và tồn kho là dữ liệu tại thời điểm truy vấn; checkout sẽ xác thực lại trước khi tạo đơn."
            },
            selected,
            selected.Count > 1
                ? new[] { "So sánh các sản phẩm này", "Kiểm tra ưu đãi hiện tại" }
                : new[] { "Xem chi tiết sản phẩm", "Tìm lựa chọn tương tự" });
    }

    private async Task<KingPhoneAiToolExecutionResult> GetProductDetailsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var productId = ReadNullableInteger(arguments, "product_id");
        var productName = ReadNullableString(arguments, "product_name");
        var products = await LoadActiveCatalogAsync(cancellationToken);
        var product = ResolveSingleProduct(products, productId, productName);

        if (product == null)
        {
            return Error(
                "get_product_details",
                "PRODUCT_NOT_FOUND",
                "Không tìm thấy sản phẩm phù hợp trong danh mục đang hoạt động của KingPhone.");
        }

        var flashByVariant = await LoadActiveFlashPricesAsync(cancellationToken);
        var variantCards = product.ProductVariants
            .Where(variant => variant.IsActive == true)
            .Select(variant => BuildProductCard(product, variant, flashByVariant))
            .Where(card => card != null)
            .Cast<KingPhoneAiProductCard>()
            .OrderByDescending(card => card.Stock > 0)
            .ThenByDescending(card => card.IsFlashSale)
            .ThenBy(card => card.Price)
            .Take(8)
            .ToList();

        return Success(
            "get_product_details",
            new
            {
                product = new
                {
                    product.ProductId,
                    product.Name,
                    brand = product.Brand?.BrandName,
                    category = product.Category?.CategoryName,
                    product.Chipset,
                    product.OperatingSystem,
                    product.BatteryCapacity,
                    product.ChargerIncluded,
                    product.ScreenSize,
                    product.ScreenTech,
                    product.RefreshRate,
                    product.RearCamera,
                    product.FrontCamera,
                    product.Weight,
                    product.Dimensions,
                    product.Description,
                    product.ReleaseDate,
                    variants = variantCards,
                    productUrl = $"/Store/Product/{product.ProductId}"
                },
                dataFreshAt = DateTime.Now
            },
            variantCards.Take(4),
            new[] { "So sánh với sản phẩm khác", "Kiểm tra ưu đãi cho sản phẩm này" });
    }

    private async Task<KingPhoneAiToolExecutionResult> CompareProductsAsync(
        JsonElement arguments,
        CancellationToken cancellationToken)
    {
        var ids = ReadIntegerArray(arguments, "product_ids").Where(id => id > 0).Distinct().Take(4).ToList();
        var names = ReadStringArray(arguments, "product_names")
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(4)
            .ToList();

        var catalog = await LoadActiveCatalogAsync(cancellationToken);
        var resolved = new List<Products>();

        foreach (var id in ids)
        {
            var product = ResolveSingleProduct(catalog, id, null);
            if (product != null && resolved.All(item => item.ProductId != product.ProductId))
            {
                resolved.Add(product);
            }
        }

        foreach (var name in names)
        {
            if (resolved.Count >= 4)
            {
                break;
            }

            var product = ResolveSingleProduct(catalog, null, name);
            if (product != null && resolved.All(item => item.ProductId != product.ProductId))
            {
                resolved.Add(product);
            }
        }

        if (resolved.Count < 2)
        {
            return Error(
                "compare_products",
                "INSUFFICIENT_PRODUCTS",
                "Cần xác định ít nhất hai sản phẩm đang hoạt động để so sánh.");
        }

        var flashByVariant = await LoadActiveFlashPricesAsync(cancellationToken);
        var cards = resolved
            .Select(product => BuildBestProductCard(product, flashByVariant, false, null, null))
            .Where(card => card != null)
            .Cast<KingPhoneAiProductCard>()
            .ToList();

        var comparison = resolved.Select(product =>
        {
            var card = cards.FirstOrDefault(item => item.ProductId == product.ProductId);
            return new
            {
                product.ProductId,
                product.Name,
                brand = product.Brand?.BrandName,
                category = product.Category?.CategoryName,
                price = card?.Price,
                originalPrice = card?.OriginalPrice,
                stock = card?.Stock ?? 0,
                product.Chipset,
                product.OperatingSystem,
                product.BatteryCapacity,
                product.ScreenSize,
                product.ScreenTech,
                product.RefreshRate,
                product.RearCamera,
                product.FrontCamera,
                product.Weight,
                product.ChargerIncluded,
                bestVariant = card?.VariantName,
                url = $"/Store/Product/{product.ProductId}"
            };
        }).ToList();

        return Success(
            "compare_products",
            new
            {
                count = comparison.Count,
                items = comparison,
                dataFreshAt = DateTime.Now,
                instruction = "Chỉ kết luận hơn/kém theo tiêu chí cụ thể mà khách đã nêu; không tuyên bố một lựa chọn tốt nhất tuyệt đối."
            },
            cards,
            new[] { "Chọn theo hiệu năng", "Chọn theo camera", "Chọn theo pin và giá" });
    }

    private async Task<KingPhoneAiToolExecutionResult> GetCurrentPromotionsAsync(
        JsonElement arguments,
        int? customerId,
        CancellationToken cancellationToken)
    {
        var cartValue = Math.Max(0m, ReadNullableDecimal(arguments, "cart_value") ?? 0m);
        var productId = ReadNullableInteger(arguments, "product_id");
        var limit = Math.Clamp(ReadInteger(arguments, "limit", 6), 1, 8);

        var voucherStates = await _promotionEngine.EvaluateCartPromotionsAsync(cartValue, customerId);
        var vouchers = voucherStates.Take(limit).Select(state => new
        {
            state.PromotionId,
            state.Code,
            state.Name,
            state.Description,
            state.MinOrderValue,
            state.DiscountType,
            state.DiscountValue,
            state.MaxDiscountAmount,
            state.IsEligible,
            state.GapAmount,
            state.EstimatedDiscountAmount
        }).ToList();

        var now = DateTime.Now;
        var flashQuery = _context.FlashSaleItems
            .AsNoTracking()
            .Where(item => item.FlashSale != null
                           && item.FlashSale.IsActive
                           && item.FlashSale.StartTime <= now
                           && item.FlashSale.EndTime >= now
                           && item.Quantity > item.Sold);

        if (productId.HasValue && productId.Value > 0)
        {
            flashQuery = flashQuery.Where(item => item.Variant != null && item.Variant.ProductId == productId.Value);
        }

        var flashRows = await flashQuery
            .Include(item => item.FlashSale)
            .Include(item => item.Variant)!
                .ThenInclude(variant => variant.Product)
                    .ThenInclude(product => product.Brand)
            .Include(item => item.Variant)!
                .ThenInclude(variant => variant.Product)
                    .ThenInclude(product => product.Category)
            .Take(limit)
            .ToListAsync(cancellationToken);

        var flashCards = flashRows
            .Where(item => item.Variant?.Product != null)
            .Select(item => BuildProductCard(
                item.Variant!.Product,
                item.Variant,
                new Dictionary<int, FlashPriceInfo>
                {
                    [item.VariantId] = new(
                        item.FlashSalePrice,
                        item.Quantity - item.Sold,
                        item.FlashSale?.Name ?? "Flash Sale",
                        item.FlashSale?.EndTime ?? now)
                }))
            .Where(card => card != null)
            .Cast<KingPhoneAiProductCard>()
            .Take(4)
            .ToList();

        return Success(
            "get_current_promotions",
            new
            {
                cartValue,
                customerScope = customerId.HasValue ? "authenticated_customer" : "guest_or_public",
                vouchers,
                flashSales = flashRows.Select(item => new
                {
                    item.FlashSaleId,
                    campaign = item.FlashSale?.Name,
                    item.VariantId,
                    productId = item.Variant?.ProductId,
                    productName = item.Variant?.Product?.Name,
                    item.FlashSalePrice,
                    remainingSlots = Math.Max(0, item.Quantity - item.Sold),
                    item.MaxPerUser,
                    endsAt = item.FlashSale?.EndTime
                }),
                finalVerificationRequired = true,
                note = "Điều kiện voucher, số suất Flash Sale, tồn kho và quyền sử dụng sẽ được kiểm tra lại tại checkout."
            },
            flashCards,
            new[] { "Tìm sản phẩm đang Flash Sale", "Tư vấn theo ngân sách sau ưu đãi" });
    }

    private async Task<KingPhoneAiToolExecutionResult> GetPersonalizedRecommendationsAsync(
        JsonElement arguments,
        int? customerId,
        CancellationToken cancellationToken)
    {
        var need = ReadNullableString(arguments, "need");
        var maxPrice = ReadNullableDecimal(arguments, "max_price");
        var limit = Math.Clamp(ReadInteger(arguments, "limit", 5), 1, 8);
        var catalog = await LoadActiveCatalogAsync(cancellationToken);
        var flashByVariant = await LoadActiveFlashPricesAsync(cancellationToken);

        var seedProductIds = new List<int>();
        var preferredBrandIds = new HashSet<int>();
        var preferredCategoryIds = new HashSet<int>();
        var candidatePriority = new Dictionary<int, int>();
        var mode = "popular_fallback";

        if (customerId.HasValue && customerId.Value > 0)
        {
            var since = DateTime.UtcNow.AddDays(-90);
            var behaviorRows = await _context.UserBehaviorLogs
                .AsNoTracking()
                .Where(log => log.CustomerId == customerId.Value && log.CreatedAt >= since)
                .OrderByDescending(log => log.CreatedAt)
                .Take(200)
                .ToListAsync(cancellationToken);

            seedProductIds = behaviorRows
                .SelectMany(log => new[] { log.TargetProductId ?? 0, log.ProductId })
                .Where(id => id > 0)
                .GroupBy(id => id)
                .OrderByDescending(group => group.Count())
                .Select(group => group.Key)
                .Take(8)
                .ToList();

            var seedProducts = catalog.Where(product => seedProductIds.Contains(product.ProductId)).ToList();
            foreach (var product in seedProducts)
            {
                preferredBrandIds.Add(product.BrandId);
                preferredCategoryIds.Add(product.CategoryId);
            }

            if (seedProductIds.Count > 0)
            {
                try
                {
                    var apriori = await _crossSellAprioriService.GetRecommendationsForProductAsync(
                        seedProductIds[0],
                        seedProductIds,
                        seedProductIds.Take(4));

                    var rank = 200;
                    foreach (var recommendation in apriori.Take(limit))
                    {
                        candidatePriority[recommendation.ProductId] = rank--;
                    }
                }
                catch (Exception exception)
                {
                    _logger.LogWarning(exception, "KingPhone AI could not obtain Apriori recommendations for customer {CustomerId}.", customerId);
                }
            }

            mode = seedProductIds.Count > 0
                ? "customer_behavior_and_apriori"
                : "authenticated_popular_fallback";
        }

        var salesRows = await _context.OrderDetails
            .AsNoTracking()
            .Where(detail => detail.VariantId.HasValue
                             && detail.Quantity.HasValue
                             && detail.Order != null
                             && detail.Order.Status != null
                             && RevenueOrderStatuses.Contains(detail.Order.Status))
            .GroupBy(detail => detail.Variant!.ProductId)
            .Select(group => new
            {
                ProductId = group.Key,
                Quantity = group.Sum(detail => detail.Quantity ?? 0)
            })
            .OrderByDescending(row => row.Quantity)
            .Take(50)
            .ToListAsync(cancellationToken);

        foreach (var salesRow in salesRows)
        {
            candidatePriority.TryAdd(salesRow.ProductId, Math.Min(120, salesRow.Quantity));
        }

        var ranked = new List<RankedProduct>();
        foreach (var product in catalog)
        {
            if (seedProductIds.Contains(product.ProductId))
            {
                continue;
            }

            var card = BuildBestProductCard(product, flashByVariant, true, null, maxPrice);
            if (card == null)
            {
                continue;
            }

            var score = ComputeProductScore(product, card, null, need);
            if (preferredBrandIds.Contains(product.BrandId)) score += 35;
            if (preferredCategoryIds.Contains(product.CategoryId)) score += 55;
            if (candidatePriority.TryGetValue(product.ProductId, out var priority)) score += priority;
            if (card.IsFlashSale) score += 30;

            ranked.Add(new RankedProduct(product, card, score));
        }

        var cards = ranked
            .OrderByDescending(item => item.Score)
            .ThenBy(item => item.Card.Price)
            .Take(limit)
            .Select(item => item.Card)
            .ToList();

        return Success(
            "get_personalized_recommendations",
            new
            {
                mode,
                isPersonalized = customerId.HasValue && seedProductIds.Count > 0,
                need,
                maxPrice,
                seedProductIds,
                count = cards.Count,
                items = cards,
                dataFreshAt = DateTime.Now,
                note = mode == "popular_fallback"
                    ? "Khách chưa đăng nhập nên đây là gợi ý phổ biến, không phải hồ sơ cá nhân."
                    : "Gợi ý kết hợp hành vi gần đây, liên kết mua kèm và dữ liệu bán hàng; không phải quyết định mua thay khách."
            },
            cards,
            new[] { "Giải thích vì sao phù hợp", "So sánh các gợi ý", "Thu hẹp theo ngân sách" });
    }

    private async Task<List<Products>> LoadActiveCatalogAsync(CancellationToken cancellationToken)
    {
        return await _context.Products
            .AsNoTracking()
            .Include(product => product.Brand)
            .Include(product => product.Category)
            .Include(product => product.ProductVariants.Where(variant => variant.IsActive == true))
            .Where(product => product.IsActive == true)
            .ToListAsync(cancellationToken);
    }

    private async Task<Dictionary<int, FlashPriceInfo>> LoadActiveFlashPricesAsync(
        CancellationToken cancellationToken)
    {
        var now = DateTime.Now;
        var rows = await _context.FlashSaleItems
            .AsNoTracking()
            .Where(item => item.FlashSale != null
                           && item.FlashSale.IsActive
                           && item.FlashSale.StartTime <= now
                           && item.FlashSale.EndTime >= now
                           && item.Quantity > item.Sold
                           && item.FlashSalePrice > 0)
            .Select(item => new
            {
                item.VariantId,
                item.FlashSalePrice,
                Remaining = item.Quantity - item.Sold,
                CampaignName = item.FlashSale!.Name,
                item.FlashSale.EndTime
            })
            .ToListAsync(cancellationToken);

        return rows
            .GroupBy(row => row.VariantId)
            .ToDictionary(
                group => group.Key,
                group =>
                {
                    var row = group.OrderBy(item => item.FlashSalePrice).First();
                    return new FlashPriceInfo(row.FlashSalePrice, row.Remaining, row.CampaignName, row.EndTime);
                });
    }

    private static Products? ResolveSingleProduct(
        IReadOnlyCollection<Products> catalog,
        int? productId,
        string? productName)
    {
        if (productId.HasValue && productId.Value > 0)
        {
            return catalog.FirstOrDefault(product => product.ProductId == productId.Value);
        }

        var normalizedName = Normalize(productName);
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            return null;
        }

        return catalog
            .Select(product => new
            {
                Product = product,
                Name = Normalize(product.Name)
            })
            .OrderByDescending(item => item.Name == normalizedName)
            .ThenByDescending(item => item.Name.StartsWith(normalizedName, StringComparison.Ordinal))
            .ThenByDescending(item => item.Name.Contains(normalizedName, StringComparison.Ordinal))
            .Select(item => item.Product)
            .FirstOrDefault(product => Normalize(product.Name).Contains(normalizedName, StringComparison.Ordinal));
    }

    private static KingPhoneAiProductCard? BuildBestProductCard(
        Products product,
        IReadOnlyDictionary<int, FlashPriceInfo> flashByVariant,
        bool requireStock,
        decimal? minPrice,
        decimal? maxPrice)
    {
        return product.ProductVariants
            .Where(variant => variant.IsActive == true)
            .Select(variant => BuildProductCard(product, variant, flashByVariant))
            .Where(card => card != null)
            .Cast<KingPhoneAiProductCard>()
            .Where(card => !requireStock || card.Stock > 0)
            .Where(card => !minPrice.HasValue || card.Price >= minPrice.Value)
            .Where(card => !maxPrice.HasValue || card.Price <= maxPrice.Value)
            .OrderByDescending(card => card.Stock > 0)
            .ThenByDescending(card => card.IsFlashSale)
            .ThenBy(card => card.Price)
            .FirstOrDefault();
    }

    private static KingPhoneAiProductCard? BuildProductCard(
        Products product,
        ProductVariants variant,
        IReadOnlyDictionary<int, FlashPriceInfo> flashByVariant)
    {
        var listPrice = Math.Max(0, variant.Price ?? 0m);
        var discountPrice = variant.DiscountPrice.HasValue
                            && variant.DiscountPrice.Value > 0
                            && (listPrice <= 0 || variant.DiscountPrice.Value < listPrice)
            ? variant.DiscountPrice.Value
            : (decimal?)null;

        var currentPrice = discountPrice ?? listPrice;
        var isFlashSale = flashByVariant.TryGetValue(variant.VariantId, out var flash)
                          && flash.Price > 0
                          && flash.Remaining > 0;

        if (isFlashSale)
        {
            currentPrice = flash!.Price;
        }

        if (currentPrice <= 0)
        {
            return null;
        }

        var originalPrice = listPrice > currentPrice ? listPrice : null;
        var stock = Math.Max(0, variant.Stock ?? 0);
        var variantName = string.Join(
            " / ",
            new[] { variant.Color, variant.Ram, variant.Storage }
                .Where(value => !string.IsNullOrWhiteSpace(value)));

        var highlights = new List<string>();
        AddHighlight(highlights, product.Chipset);
        AddHighlight(highlights, product.BatteryCapacity.HasValue ? $"Pin {product.BatteryCapacity.Value:N0} mAh" : null);
        AddHighlight(highlights, product.RefreshRate.HasValue ? $"Màn hình {product.RefreshRate.Value} Hz" : product.ScreenTech);
        AddHighlight(highlights, product.RearCamera);

        return new KingPhoneAiProductCard
        {
            ProductId = product.ProductId,
            VariantId = variant.VariantId,
            Name = product.Name,
            VariantName = variantName,
            BrandName = product.Brand?.BrandName ?? string.Empty,
            CategoryName = product.Category?.CategoryName ?? string.Empty,
            Price = currentPrice,
            OriginalPrice = originalPrice,
            IsFlashSale = isFlashSale,
            Stock = stock,
            StockText = stock <= 0 ? "Tạm hết hàng" : stock <= 5 ? $"Sắp hết: còn {stock}" : $"Còn {stock} sản phẩm",
            ImageUrl = !string.IsNullOrWhiteSpace(variant.ImageUrl)
                ? variant.ImageUrl
                : product.MainImage ?? "/images/products/default-product.png",
            Url = $"/Store/Product/{product.ProductId}",
            Highlights = highlights.Take(4).ToList()
        };
    }

    private static int ComputeProductScore(
        Products product,
        KingPhoneAiProductCard card,
        string? keyword,
        string? need)
    {
        var document = Normalize(string.Join(
            " ",
            product.Name,
            product.Brand?.BrandName,
            product.Category?.CategoryName,
            product.Chipset,
            product.OperatingSystem,
            product.ScreenTech,
            product.RearCamera,
            product.FrontCamera,
            product.Description,
            card.VariantName));

        var normalizedKeyword = Normalize(keyword);
        var normalizedNeed = Normalize(need);
        var hasSearchConstraint = !string.IsNullOrWhiteSpace(normalizedKeyword)
                                  || !string.IsNullOrWhiteSpace(normalizedNeed);
        var matchedConstraint = false;
        var score = hasSearchConstraint ? 0 : 1;

        if (!string.IsNullOrWhiteSpace(normalizedKeyword))
        {
            var normalizedName = Normalize(product.Name);
            if (normalizedName == normalizedKeyword)
            {
                score += 300;
                matchedConstraint = true;
            }
            if (normalizedName.StartsWith(normalizedKeyword, StringComparison.Ordinal))
            {
                score += 220;
                matchedConstraint = true;
            }
            if (normalizedName.Contains(normalizedKeyword, StringComparison.Ordinal))
            {
                score += 160;
                matchedConstraint = true;
            }

            foreach (var token in Tokenize(normalizedKeyword))
            {
                if (document.Contains(token, StringComparison.Ordinal))
                {
                    score += token.Length >= 4 ? 35 : 15;
                    matchedConstraint = true;
                }
            }
        }

        foreach (var token in Tokenize(normalizedNeed))
        {
            var needScore = ScoreNeedToken(product, document, token);
            if (needScore > 0)
            {
                score += needScore;
                matchedConstraint = true;
            }
        }

        if (hasSearchConstraint && !matchedConstraint)
        {
            return 0;
        }

        if (card.Stock > 0) score += 20;
        if (card.IsFlashSale) score += 25;
        if (card.OriginalPrice.HasValue) score += 10;
        return score;
    }

    private static int ScoreNeedToken(Products product, string document, string token)
    {
        if (string.IsNullOrWhiteSpace(token)) return 0;
        var score = document.Contains(token, StringComparison.Ordinal) ? 25 : 0;

        if (token is "game" or "gaming" or "choi")
        {
            if (!string.IsNullOrWhiteSpace(product.Chipset)) score += 35;
            if ((product.RefreshRate ?? 0) >= 90) score += 35;
            if ((product.BatteryCapacity ?? 0) >= 4500) score += 20;
        }
        else if (token is "camera" or "chup" or "anh")
        {
            if (!string.IsNullOrWhiteSpace(product.RearCamera)) score += 45;
            if (!string.IsNullOrWhiteSpace(product.FrontCamera)) score += 20;
        }
        else if (token is "pin" or "battery" or "lau")
        {
            if ((product.BatteryCapacity ?? 0) >= 5000) score += 55;
            else if ((product.BatteryCapacity ?? 0) >= 4500) score += 35;
        }
        else if (token is "man" or "hinh" or "display")
        {
            if (!string.IsNullOrWhiteSpace(product.ScreenTech)) score += 30;
            if ((product.RefreshRate ?? 0) >= 120) score += 30;
        }
        else if (token is "nhe" or "gon")
        {
            if ((product.Weight ?? decimal.MaxValue) <= 190) score += 35;
        }
        else if (token is "hoc" or "tap" or "van" or "phong" or "cong" or "viec")
        {
            if (!string.IsNullOrWhiteSpace(product.OperatingSystem)) score += 20;
            if ((product.BatteryCapacity ?? 0) >= 4500) score += 25;
            if ((product.Weight ?? decimal.MaxValue) <= 200) score += 20;
        }

        return score;
    }

    private static IEnumerable<string> Tokenize(string? value)
    {
        return (value ?? string.Empty)
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(token => token.Length >= 2)
            .Distinct(StringComparer.Ordinal);
    }

    private static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var decomposed = value.Trim().ToLowerInvariant().Normalize(NormalizationForm.FormD);
        var builder = new StringBuilder(decomposed.Length);
        foreach (var character in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(character) == UnicodeCategory.NonSpacingMark)
            {
                continue;
            }

            builder.Append(char.IsLetterOrDigit(character) ? character : ' ');
        }

        return string.Join(
            " ",
            builder.ToString()
                .Normalize(NormalizationForm.FormC)
                .Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static void AddHighlight(ICollection<string> highlights, string? value)
    {
        if (!string.IsNullOrWhiteSpace(value))
        {
            highlights.Add(value.Trim());
        }
    }

    private static object NullableString(string description) => new
    {
        type = new[] { "string", "null" },
        description
    };

    private static object NullableNumber(string description) => new
    {
        type = new[] { "number", "null" },
        description
    };

    private static object NullableInteger(string description) => new
    {
        type = new[] { "integer", "null" },
        description
    };

    private static string? ReadNullableString(JsonElement arguments, string propertyName)
    {
        if (!arguments.TryGetProperty(propertyName, out var element)
            || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.String
            ? element.GetString()?.Trim()
            : null;
    }

    private static decimal? ReadNullableDecimal(JsonElement arguments, string propertyName)
    {
        if (!arguments.TryGetProperty(propertyName, out var element)
            || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        if (element.ValueKind == JsonValueKind.Number && element.TryGetDecimal(out var value))
        {
            return value;
        }

        return null;
    }

    private static int? ReadNullableInteger(JsonElement arguments, string propertyName)
    {
        if (!arguments.TryGetProperty(propertyName, out var element)
            || element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined)
        {
            return null;
        }

        return element.ValueKind == JsonValueKind.Number && element.TryGetInt32(out var value)
            ? value
            : null;
    }

    private static int ReadInteger(JsonElement arguments, string propertyName, int fallback)
    {
        return ReadNullableInteger(arguments, propertyName) ?? fallback;
    }

    private static bool ReadBoolean(JsonElement arguments, string propertyName, bool fallback)
    {
        if (!arguments.TryGetProperty(propertyName, out var element))
        {
            return fallback;
        }

        return element.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            _ => fallback
        };
    }

    private static List<int> ReadIntegerArray(JsonElement arguments, string propertyName)
    {
        if (!arguments.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.Array)
        {
            return new List<int>();
        }

        return element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.Number && item.TryGetInt32(out _))
            .Select(item => item.GetInt32())
            .ToList();
    }

    private static List<string> ReadStringArray(JsonElement arguments, string propertyName)
    {
        if (!arguments.TryGetProperty(propertyName, out var element)
            || element.ValueKind != JsonValueKind.Array)
        {
            return new List<string>();
        }

        return element.EnumerateArray()
            .Where(item => item.ValueKind == JsonValueKind.String)
            .Select(item => item.GetString()?.Trim() ?? string.Empty)
            .Where(item => !string.IsNullOrWhiteSpace(item))
            .ToList();
    }

    private static KingPhoneAiToolExecutionResult Success(
        string toolName,
        object output,
        IEnumerable<KingPhoneAiProductCard>? products = null,
        IEnumerable<string>? quickReplies = null)
    {
        return new KingPhoneAiToolExecutionResult
        {
            ToolName = toolName,
            OutputJson = JsonSerializer.Serialize(
                new
                {
                    success = true,
                    data = output
                },
                ToolJsonOptions),
            Products = products?
                .GroupBy(product => product.VariantId)
                .Select(group => group.First())
                .Take(8)
                .ToList() ?? new List<KingPhoneAiProductCard>(),
            QuickReplies = quickReplies?
                .Where(reply => !string.IsNullOrWhiteSpace(reply))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .Take(4)
                .ToList() ?? new List<string>()
        };
    }

    private static KingPhoneAiToolExecutionResult Error(
        string toolName,
        string code,
        string message)
    {
        return new KingPhoneAiToolExecutionResult
        {
            ToolName = toolName,
            OutputJson = JsonSerializer.Serialize(
                new
                {
                    success = false,
                    error = new { code, message }
                },
                ToolJsonOptions)
        };
    }

    private sealed record FlashPriceInfo(
        decimal Price,
        int Remaining,
        string CampaignName,
        DateTime EndTime);

    private sealed record RankedProduct(
        Products Product,
        KingPhoneAiProductCard Card,
        int Score);
}
