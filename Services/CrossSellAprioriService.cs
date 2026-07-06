using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels.Admin;
using TMDT_LT.Models.ViewModels.Storefront;

namespace TMDT_LT.Services
{
    public interface ICrossSellAprioriService
    {
        Task<CrossSellSettings> GetSettingsAsync();
        Task<List<CrossSellRecommendationVM>> GetRecommendationsForProductAsync(int baseProductId, IEnumerable<int>? excludedProductIds = null, IEnumerable<int>? contextProductIds = null);
        Task<Dictionary<int, List<CrossSellRecommendationVM>>> GetRecommendationsForProductsAsync(IEnumerable<int> baseProductIds, IEnumerable<int>? excludedProductIds = null, IEnumerable<int>? contextProductIds = null);
        Task<Dictionary<int, CrossSellRecommendationVM>> GetCartAppliedDiscountsAsync(IEnumerable<int> contextProductIds);
        Task<List<AprioriRulePreviewVM>> GetTopRulePreviewsAsync(int take = 30);
    }

    public class CrossSellAprioriService : ICrossSellAprioriService
    {
        private readonly ApplicationDbContext _context;

        public CrossSellAprioriService(ApplicationDbContext context)
        {
            _context = context;
        }

        public async Task<CrossSellSettings> GetSettingsAsync()
        {
            var settings = await _context.CrossSellSettings.AsNoTracking().OrderBy(x => x.CrossSellSettingsId).FirstOrDefaultAsync();
            return NormalizeSettings(settings ?? new CrossSellSettings { CrossSellSettingsId = 1 });
        }

        public async Task<List<CrossSellRecommendationVM>> GetRecommendationsForProductAsync(int baseProductId, IEnumerable<int>? excludedProductIds = null, IEnumerable<int>? contextProductIds = null)
        {
            var map = await GetRecommendationsForProductsAsync(new[] { baseProductId }, excludedProductIds, contextProductIds ?? new[] { baseProductId });
            return map.TryGetValue(baseProductId, out var list) ? list : new List<CrossSellRecommendationVM>();
        }

        public async Task<Dictionary<int, List<CrossSellRecommendationVM>>> GetRecommendationsForProductsAsync(IEnumerable<int> baseProductIds, IEnumerable<int>? excludedProductIds = null, IEnumerable<int>? contextProductIds = null)
        {
            var bases = baseProductIds.Where(x => x > 0).Distinct().ToList();
            var result = bases.ToDictionary(x => x, _ => new List<CrossSellRecommendationVM>());
            if (!bases.Any()) return result;

            var settings = await GetSettingsAsync();
            if (!settings.IsEnabled) return result;

            var excluded = new HashSet<int>((excludedProductIds ?? Enumerable.Empty<int>()).Where(x => x > 0));
            var context = new HashSet<int>((contextProductIds ?? bases).Where(x => x > 0));
            foreach (var baseId in bases) context.Add(baseId);

            var rules = await MineAprioriRulesAsync(settings);
            foreach (int baseId in bases)
            {
                var rankedRules = rules
                    .Where(r => r.AntecedentProductIds.Contains(baseId)
                        && r.AntecedentProductIds.All(context.Contains)
                        && !excluded.Contains(r.ConsequentProductId)
                        && r.ConsequentProductId != baseId)
                    .GroupBy(r => r.ConsequentProductId)
                    .Select(g => g.OrderByDescending(x => x.Confidence)
                                  .ThenByDescending(x => x.Lift)
                                  .ThenByDescending(x => x.SupportCount)
                                  .First())
                    .OrderByDescending(x => x.Confidence)
                    .ThenByDescending(x => x.Lift)
                    .ThenByDescending(x => x.SupportCount)
                    .Take(settings.MaxRecommendationsPerProduct)
                    .ToList();

                result[baseId] = await HydrateRecommendationsAsync(rankedRules, settings);

                if (!result[baseId].Any() && settings.AllowFallbackWhenNoRule)
                {
                    result[baseId] = await BuildFallbackRecommendationsAsync(baseId, excluded, settings.MaxRecommendationsPerProduct, settings.ExcludeOutOfStock);
                }
            }

            return result;
        }

        public async Task<Dictionary<int, CrossSellRecommendationVM>> GetCartAppliedDiscountsAsync(IEnumerable<int> contextProductIds)
        {
            var context = contextProductIds.Where(x => x > 0).Distinct().ToHashSet();
            if (context.Count < 2) return new Dictionary<int, CrossSellRecommendationVM>();

            var settings = await GetSettingsAsync();
            if (!settings.IsEnabled || !settings.IsBundleDiscountEnabled || settings.BundleDiscountValue <= 0)
            {
                return new Dictionary<int, CrossSellRecommendationVM>();
            }

            var rules = await MineAprioriRulesAsync(settings);
            var eligibleRules = rules
                .Where(r => context.Contains(r.ConsequentProductId)
                    && r.AntecedentProductIds.All(context.Contains)
                    && !r.AntecedentProductIds.Contains(r.ConsequentProductId))
                .GroupBy(r => r.ConsequentProductId)
                .Select(g => g.OrderByDescending(x => x.Confidence)
                              .ThenByDescending(x => x.Lift)
                              .ThenByDescending(x => x.SupportCount)
                              .First())
                .ToList();

            var hydrated = await HydrateRecommendationsAsync(eligibleRules, settings);
            return hydrated
                .Where(x => x.IsBundleDiscountApplied && x.BundleDiscountAmount > 0)
                .GroupBy(x => x.ProductId)
                .ToDictionary(g => g.Key, g => g.First());
        }

        public async Task<List<AprioriRulePreviewVM>> GetTopRulePreviewsAsync(int take = 30)
        {
            var settings = await GetSettingsAsync();
            var rules = await MineAprioriRulesAsync(settings);
            var productIds = rules.SelectMany(r => r.AntecedentProductIds.Append(r.ConsequentProductId)).Distinct().ToList();
            var names = await _context.Products
                .Where(p => productIds.Contains(p.ProductId))
                .Select(p => new { p.ProductId, p.Name })
                .ToDictionaryAsync(x => x.ProductId, x => x.Name);

            return rules
                .OrderByDescending(r => r.Confidence)
                .ThenByDescending(r => r.Lift)
                .ThenByDescending(r => r.SupportCount)
                .Take(Math.Clamp(take, 1, 100))
                .Select(r => new AprioriRulePreviewVM
                {
                    AntecedentNames = string.Join(" + ", r.AntecedentProductIds.Select(id => names.TryGetValue(id, out var n) ? n : $"SP #{id}")),
                    ConsequentName = names.TryGetValue(r.ConsequentProductId, out var name) ? name : $"SP #{r.ConsequentProductId}",
                    SupportCount = r.SupportCount,
                    SupportPercent = r.SupportPercent,
                    Confidence = r.Confidence,
                    Lift = r.Lift,
                    BundleDiscountPreview = BuildBundleDiscountPreview(settings)
                })
                .ToList();
        }

        private async Task<List<AprioriAssociationRule>> MineAprioriRulesAsync(CrossSellSettings rawSettings)
        {
            var settings = NormalizeSettings(rawSettings);
            if (!settings.IsEnabled) return new List<AprioriAssociationRule>();

            var query = _context.OrderDetails
                .Where(d => d.OrderId.HasValue && d.VariantId.HasValue && d.Order != null && d.Variant != null);

            if (settings.AnalysisWindowDays > 0)
            {
                var since = DateTime.Now.AddDays(-settings.AnalysisWindowDays);
                query = query.Where(d => d.Order!.OrderDate.HasValue && d.Order.OrderDate.Value >= since);
            }

            var allowedStatuses = ParseStatuses(settings.AllowedOrderStatuses);
            if (settings.OnlyCompletedOrders && allowedStatuses.Count > 0)
            {
                query = query.Where(d => d.Order!.Status != null && allowedStatuses.Contains(d.Order.Status));
            }

            var raw = await query
                .Select(d => new
                {
                    OrderId = d.OrderId!.Value,
                    ProductId = d.Variant!.ProductId
                })
                .ToListAsync();

            var baskets = raw
                .GroupBy(x => x.OrderId)
                .Select(g => g.Select(x => x.ProductId).Where(id => id > 0).Distinct().OrderBy(id => id).ToArray())
                .Where(x => x.Length >= 2)
                .ToList();

            if (baskets.Count == 0) return new List<AprioriAssociationRule>();

            int supportThreshold = Math.Max(settings.MinSupportCount, (int)Math.Ceiling(baskets.Count * settings.MinSupportPercent));
            supportThreshold = Math.Max(1, supportThreshold);

            var basketSets = baskets.Select(b => new HashSet<int>(b)).ToList();
            var frequent = new Dictionary<string, int>();

            var l1 = baskets
                .SelectMany(b => b)
                .GroupBy(id => id)
                .Where(g => g.Count() >= supportThreshold)
                .ToDictionary(g => Key(new[] { g.Key }), g => g.Count());

            foreach (var pair in l1) frequent[pair.Key] = pair.Value;

            var previousLevel = l1.Keys.ToList();
            int maxK = Math.Clamp(settings.MaxItemsetSize, 2, 4);

            for (int k = 2; k <= maxK && previousLevel.Any(); k++)
            {
                var candidates = GenerateCandidates(previousLevel, k).ToList();
                if (!candidates.Any()) break;

                var candidateItems = candidates.Select(kv => ParseKey(kv)).ToDictionary(ids => Key(ids), ids => ids);
                var counts = candidates.ToDictionary(c => c, _ => 0);

                foreach (var basket in basketSets)
                {
                    foreach (var candidate in candidateItems)
                    {
                        if (candidate.Value.All(basket.Contains)) counts[candidate.Key]++;
                    }
                }

                previousLevel = counts
                    .Where(x => x.Value >= supportThreshold)
                    .Select(x => x.Key)
                    .ToList();

                foreach (var itemsetKey in previousLevel)
                {
                    frequent[itemsetKey] = counts[itemsetKey];
                }
            }

            var rules = new List<AprioriAssociationRule>();
            foreach (var itemset in frequent.Where(x => ParseKey(x.Key).Length >= 2))
            {
                var items = ParseKey(itemset.Key);
                int itemsetSupport = itemset.Value;

                for (int subsetSize = 1; subsetSize < items.Length; subsetSize++)
                {
                    foreach (var antecedent in Combinations(items, subsetSize))
                    {
                        var consequent = items.Except(antecedent).OrderBy(x => x).ToArray();
                        if (consequent.Length != 1) continue;

                        var antecedentKey = Key(antecedent);
                        var consequentKey = Key(consequent);
                        if (!frequent.TryGetValue(antecedentKey, out int antecedentSupport)) continue;
                        if (!frequent.TryGetValue(consequentKey, out int consequentSupport)) continue;
                        if (antecedentSupport <= 0 || consequentSupport <= 0) continue;

                        decimal confidence = (decimal)itemsetSupport / antecedentSupport;
                        decimal consequentSupportPercent = (decimal)consequentSupport / baskets.Count;
                        decimal lift = consequentSupportPercent == 0 ? 0 : confidence / consequentSupportPercent;
                        decimal supportPercent = (decimal)itemsetSupport / baskets.Count;

                        if (confidence < settings.MinConfidence) continue;
                        if (lift < settings.MinLift) continue;

                        rules.Add(new AprioriAssociationRule
                        {
                            AntecedentProductIds = antecedent.OrderBy(x => x).ToArray(),
                            ConsequentProductId = consequent[0],
                            SupportCount = itemsetSupport,
                            SupportPercent = supportPercent,
                            Confidence = confidence,
                            Lift = lift
                        });
                    }
                }
            }

            return rules;
        }

        private async Task<List<CrossSellRecommendationVM>> HydrateRecommendationsAsync(List<AprioriAssociationRule> rules, CrossSellSettings settings)
        {
            if (!rules.Any()) return new List<CrossSellRecommendationVM>();
            var ids = rules.Select(x => x.ConsequentProductId).Distinct().ToList();

            var products = await _context.Products
                .Include(p => p.Brand)
                .Include(p => p.ProductVariants.Where(v => v.IsActive == true))
                .Where(p => p.IsActive == true && ids.Contains(p.ProductId))
                .ToListAsync();

            var result = new List<CrossSellRecommendationVM>();
            foreach (var rule in rules)
            {
                var product = products.FirstOrDefault(p => p.ProductId == rule.ConsequentProductId);
                if (product == null) continue;
                var variant = GetBestVariant(product, settings.ExcludeOutOfStock);
                if (variant == null) continue;

                decimal basePrice = GetDisplayPrice(variant);
                decimal price = ApplyBundleDiscount(basePrice, settings, out decimal bundleDiscountAmount);
                bool hasBundleDiscount = bundleDiscountAmount > 0 && price < basePrice;
                decimal? oldPrice = hasBundleDiscount
                    ? basePrice
                    : (variant.Price.HasValue && variant.Price.Value > price ? variant.Price.Value : null);

                result.Add(new CrossSellRecommendationVM
                {
                    ProductId = product.ProductId,
                    PrimaryVariantId = variant.VariantId,
                    ProductName = product.Name,
                    BrandName = product.Brand?.BrandName ?? string.Empty,
                    ImageUrl = !string.IsNullOrWhiteSpace(variant.ImageUrl) ? variant.ImageUrl : (product.MainImage ?? "/images/productDefault.png"),
                    Price = price,
                    OldPrice = oldPrice,
                    IsBundleDiscountApplied = hasBundleDiscount,
                    BundleOriginalPrice = basePrice,
                    BundleDiscountAmount = bundleDiscountAmount,
                    BundleDiscountLabel = settings.BundleDiscountLabel,
                    SupportCount = rule.SupportCount,
                    SupportPercent = rule.SupportPercent,
                    Confidence = rule.Confidence,
                    Lift = rule.Lift
                });
            }

            return result;
        }

        private async Task<List<CrossSellRecommendationVM>> BuildFallbackRecommendationsAsync(int baseProductId, HashSet<int> excluded, int take, bool excludeOutOfStock)
        {
            var current = await _context.Products.AsNoTracking().FirstOrDefaultAsync(p => p.ProductId == baseProductId);
            if (current == null) return new List<CrossSellRecommendationVM>();

            var products = await _context.Products
                .Include(p => p.Brand)
                .Include(p => p.ProductVariants.Where(v => v.IsActive == true))
                .Where(p => p.IsActive == true
                    && p.ProductId != baseProductId
                    && !excluded.Contains(p.ProductId)
                    && p.CategoryId == current.CategoryId)
                .OrderByDescending(p => p.CreatedDate)
                .Take(Math.Clamp(take, 1, 24) * 3)
                .ToListAsync();

            return products
                .Select(p => new { Product = p, Variant = GetBestVariant(p, excludeOutOfStock) })
                .Where(x => x.Variant != null)
                .Take(take)
                .Select(x =>
                {
                    var v = x.Variant!;
                    decimal basePrice = GetDisplayPrice(v);
                    return new CrossSellRecommendationVM
                    {
                        ProductId = x.Product.ProductId,
                        PrimaryVariantId = v.VariantId,
                        ProductName = x.Product.Name,
                        BrandName = x.Product.Brand?.BrandName ?? string.Empty,
                        ImageUrl = !string.IsNullOrWhiteSpace(v.ImageUrl) ? v.ImageUrl : (x.Product.MainImage ?? "/images/productDefault.png"),
                        Price = basePrice,
                        OldPrice = v.Price.HasValue && v.Price.Value > basePrice ? v.Price.Value : null
                    };
                })
                .ToList();
        }

        private static ProductVariants? GetBestVariant(Products product, bool excludeOutOfStock)
        {
            var variants = product.ProductVariants?.Where(v => v.IsActive == true) ?? Enumerable.Empty<ProductVariants>();
            if (excludeOutOfStock) variants = variants.Where(v => (v.Stock ?? 0) > 0);
            return variants.OrderBy(GetDisplayPrice).FirstOrDefault();
        }

        private static decimal GetDisplayPrice(ProductVariants variant)
        {
            return variant.DiscountPrice.HasValue && variant.DiscountPrice.Value > 0
                ? variant.DiscountPrice.Value
                : (variant.Price ?? 0m);
        }

        private static decimal ApplyBundleDiscount(decimal basePrice, CrossSellSettings settings, out decimal discountAmount)
        {
            discountAmount = 0m;
            if (!settings.IsBundleDiscountEnabled || settings.BundleDiscountValue <= 0 || basePrice <= 0) return basePrice;

            if (settings.BundleDiscountType == 1)
            {
                discountAmount = Math.Min(basePrice, settings.BundleDiscountValue);
            }
            else
            {
                var percent = Math.Clamp(settings.BundleDiscountValue, 0m, 100m);
                discountAmount = Math.Round(basePrice * percent / 100m, 0, MidpointRounding.AwayFromZero);
            }

            if (discountAmount <= 0) return basePrice;
            return Math.Max(0m, basePrice - discountAmount);
        }

        private static string BuildBundleDiscountPreview(CrossSellSettings settings)
        {
            if (!settings.IsBundleDiscountEnabled || settings.BundleDiscountValue <= 0) return "Không áp dụng";
            return settings.BundleDiscountType == 1
                ? $"Giảm {settings.BundleDiscountValue:N0} đ cho sản phẩm mua kèm"
                : $"Giảm {settings.BundleDiscountValue:0.##}% cho sản phẩm mua kèm";
        }

        private static CrossSellSettings NormalizeSettings(CrossSellSettings settings)
        {
            settings.MinSupportCount = Math.Max(1, settings.MinSupportCount);
            settings.MinSupportPercent = Math.Clamp(settings.MinSupportPercent, 0m, 1m);
            settings.MinConfidence = Math.Clamp(settings.MinConfidence, 0m, 1m);
            settings.MinLift = Math.Clamp(settings.MinLift, 0m, 100m);
            settings.MaxItemsetSize = Math.Clamp(settings.MaxItemsetSize, 2, 4);
            settings.MaxRecommendationsPerProduct = Math.Clamp(settings.MaxRecommendationsPerProduct, 1, 24);
            settings.AnalysisWindowDays = Math.Clamp(settings.AnalysisWindowDays, 0, 3650);
            settings.BundleDiscountType = settings.BundleDiscountType == 1 ? 1 : 0;
            settings.BundleDiscountValue = Math.Clamp(settings.BundleDiscountValue, 0m, 100000000m);
            settings.BundleDiscountLabel = string.IsNullOrWhiteSpace(settings.BundleDiscountLabel)
                ? "Ưu đãi mua kèm"
                : settings.BundleDiscountLabel.Trim();
            settings.AllowedOrderStatuses = string.IsNullOrWhiteSpace(settings.AllowedOrderStatuses)
                ? "Đã hoàn thành,Hoàn thành,Đã giao,Completed"
                : settings.AllowedOrderStatuses.Trim();
            return settings;
        }

        private static HashSet<string> ParseStatuses(string? raw)
        {
            return new HashSet<string>((raw ?? string.Empty)
                .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries), StringComparer.OrdinalIgnoreCase);
        }

        private static IEnumerable<string> GenerateCandidates(List<string> previousLevelKeys, int k)
        {
            var prevSets = previousLevelKeys.Select(ParseKey).ToList();
            var prevKeySet = previousLevelKeys.ToHashSet();
            var candidates = new HashSet<string>();

            for (int i = 0; i < prevSets.Count; i++)
            {
                for (int j = i + 1; j < prevSets.Count; j++)
                {
                    var union = prevSets[i].Concat(prevSets[j]).Distinct().OrderBy(x => x).ToArray();
                    if (union.Length != k) continue;

                    bool allSubsetsFrequent = Combinations(union, k - 1).All(sub => prevKeySet.Contains(Key(sub)));
                    if (allSubsetsFrequent) candidates.Add(Key(union));
                }
            }

            return candidates;
        }

        private static IEnumerable<int[]> Combinations(int[] source, int size)
        {
            int[] buffer = new int[size];
            foreach (var combo in CombinationsRecursive(source, size, 0, 0, buffer))
            {
                yield return combo;
            }
        }

        private static IEnumerable<int[]> CombinationsRecursive(int[] source, int size, int sourceIndex, int bufferIndex, int[] buffer)
        {
            if (bufferIndex == size)
            {
                yield return buffer.ToArray();
                yield break;
            }

            for (int i = sourceIndex; i <= source.Length - (size - bufferIndex); i++)
            {
                buffer[bufferIndex] = source[i];
                foreach (var combo in CombinationsRecursive(source, size, i + 1, bufferIndex + 1, buffer))
                {
                    yield return combo;
                }
            }
        }

        private static string Key(IEnumerable<int> ids) => string.Join(',', ids.OrderBy(x => x));
        private static int[] ParseKey(string key) => key.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(int.Parse).ToArray();

        private class AprioriAssociationRule
        {
            public int[] AntecedentProductIds { get; set; } = Array.Empty<int>();
            public int ConsequentProductId { get; set; }
            public int SupportCount { get; set; }
            public decimal SupportPercent { get; set; }
            public decimal Confidence { get; set; }
            public decimal Lift { get; set; }
        }
    }
}
