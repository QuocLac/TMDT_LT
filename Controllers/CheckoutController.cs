using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
using System.Text.Json;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels.Storefront;
using TMDT_LT.Services;

namespace TMDT_LT.Controllers
{
    [Authorize]
    public class CheckoutController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PromotionEngine _promotionEngine;
        private readonly VnPayService _vnPayService;
        private readonly GhnService _ghnService;
        private readonly ICrossSellAprioriService _crossSellAprioriService;
        private readonly IOrderInventoryService _orderInventoryService;

        public CheckoutController(
            ApplicationDbContext context,
            PromotionEngine promotionEngine,
            VnPayService vnPayService,
            GhnService ghnService,
            ICrossSellAprioriService crossSellAprioriService,
            IOrderInventoryService orderInventoryService)
        {
            _context = context;
            _promotionEngine = promotionEngine;
            _vnPayService = vnPayService;
            _ghnService = ghnService;
            _crossSellAprioriService = crossSellAprioriService;
            _orderInventoryService = orderInventoryService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? selectedItems, int? buyNowVariantId, int? buyNowQty)
        {
            int customerId = int.Parse(User.FindFirstValue("CustomerId") ?? "0");
            var customer = await _context.Customer.Include(c => c.Account).FirstOrDefaultAsync(c => c.CustomerId == customerId);
            if (customer == null) return RedirectToAction("Login", "Account");

            var cartVM = new List<CartItemVM>();

            if (buyNowVariantId.HasValue && buyNowVariantId > 0 && buyNowQty.HasValue && buyNowQty > 0)
            {
                var variant = await _context.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.VariantId == buyNowVariantId && v.IsActive == true);
                if (variant == null) return RedirectToAction("Index", "Store");

                cartVM.Add(new CartItemVM
                {
                    VariantId = variant.VariantId,
                    ProductName = variant.Product?.Name ?? "",
                    Color = variant.Color ?? "",
                    Storage = variant.Storage ?? "",
                    ImageUrl = variant.ImageUrl ?? variant.Product?.MainImage ?? "",
                    Quantity = buyNowQty.Value,
                    Stock = variant.Stock ?? 0
                });

                ViewBag.IsBuyNow = true;
                ViewBag.BuyNowVariantId = buyNowVariantId;
                ViewBag.BuyNowQty = buyNowQty;
            }
            else
            {
                if (string.IsNullOrWhiteSpace(selectedItems)) return RedirectToAction("Index", "Cart");

                var selectedIds = selectedItems.Split(',').Where(x => int.TryParse(x, out _)).Select(int.Parse).ToList();
                var cartItems = await _context.CartItems
                    .Include(c => c.Variant).ThenInclude(v => v!.Product)
                    .Where(c => c.CustomerId == customerId && selectedIds.Contains(c.VariantId ?? 0))
                    .ToListAsync();

                if (!cartItems.Any()) return RedirectToAction("Index", "Cart");

                cartVM = cartItems.Select(c => new CartItemVM
                {
                    VariantId = c.VariantId ?? 0,
                    ProductName = c.Variant?.Product?.Name ?? "",
                    Color = c.Variant?.Color ?? "",
                    Storage = c.Variant?.Storage ?? "",
                    ImageUrl = c.Variant?.ImageUrl ?? c.Variant?.Product?.MainImage ?? "",
                    Quantity = c.Quantity ?? 1,
                    Stock = c.Variant?.Stock ?? 0
                }).ToList();

                ViewBag.SelectedItems = selectedItems;
            }

            await RefreshCartMetadataAsync(cartVM, customerId);
            cartVM = cartVM.Where(c => c.Quantity > 0).ToList();
            decimal subtotal = cartVM.Sum(c => c.TotalPrice);

            var voucherStates = await _promotionEngine.EvaluateCartPromotionsAsync(subtotal, customerId);
            ViewBag.VoucherStates = voucherStates;
            var bestVoucher = voucherStates.FirstOrDefault(v => v.IsEligible);
            ViewBag.BestVoucher = bestVoucher;
            decimal initialDiscount = bestVoucher != null ? bestVoucher.EstimatedDiscountAmount : 0;

            var model = new CheckoutVM
            {
                CartItems = cartVM,
                Subtotal = subtotal,
                DiscountAmount = initialDiscount,
                FinalTotal = subtotal - initialDiscount,
                CustomerName = customer.FullName,
                CustomerPhone = customer.Phone ?? "",
                CustomerEmail = customer.Account.Email ?? "",
                SavedAddresses = await _context.Address.Where(a => a.CustomerId == customerId).ToListAsync(),
                AvailableCarriers = await _context.ShippingCarriers.Where(c => c.IsActive).ToListAsync()
            };

            return View(model);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PlaceOrder(int SelectedAddressId, string PaymentMethod, string? AppliedVoucherCode, string? selectedItems, int? buyNowVariantId, int? buyNowQty)
        {
            int customerId = int.Parse(User.FindFirstValue("CustomerId") ?? "0");

            // Form cũ từng dùng name="paymentMethod". Đọc cả hai key để tránh submit xong quay lại trang checkout.
            if (string.IsNullOrWhiteSpace(PaymentMethod) && Request.HasFormContentType)
            {
                PaymentMethod = Request.Form["PaymentMethod"].ToString();
                if (string.IsNullOrWhiteSpace(PaymentMethod))
                {
                    PaymentMethod = Request.Form["paymentMethod"].ToString();
                }
            }

            PaymentMethod = string.IsNullOrWhiteSpace(PaymentMethod) ? "COD" : PaymentMethod.Trim();
            bool isVnPay = PaymentMethod.Equals(PaymentMethods.VnPay, StringComparison.OrdinalIgnoreCase);
            bool isBankTransfer = PaymentMethod.Equals(PaymentMethods.BankTransfer, StringComparison.OrdinalIgnoreCase);
            bool isCod = PaymentMethod.Equals(PaymentMethods.Cod, StringComparison.OrdinalIgnoreCase);

            if (!isCod && !isVnPay && !isBankTransfer)
            {
                PaymentMethod = PaymentMethods.Cod;
                isCod = true;
            }

            var address = await _context.Address.Include(a => a.Customer).FirstOrDefaultAsync(a => a.AddressId == SelectedAddressId && a.CustomerId == customerId);
            if (address == null)
            {
                TempData["Error"] = "Vui lòng chọn địa chỉ giao hàng hợp lệ.";
                return RedirectToAction("Index", new { selectedItems, buyNowVariantId, buyNowQty });
            }

            var cartVM = new List<CartItemVM>();
            var dbCartItemsToRemove = new List<CartItems>();

            if (buyNowVariantId.HasValue && buyNowQty.HasValue)
            {
                var variant = await _context.ProductVariants.FirstOrDefaultAsync(v => v.VariantId == buyNowVariantId && v.IsActive == true);
                if (variant == null) return RedirectToAction("Index", "Store");

                cartVM.Add(new CartItemVM { VariantId = variant.VariantId, Quantity = buyNowQty.Value });
            }
            else
            {
                if (string.IsNullOrWhiteSpace(selectedItems)) return RedirectToAction("Index", "Cart");
                var selectedIds = selectedItems.Split(',').Where(x => int.TryParse(x, out _)).Select(int.Parse).ToList();
                dbCartItemsToRemove = await _context.CartItems.Where(c => c.CustomerId == customerId && selectedIds.Contains(c.VariantId ?? 0)).ToListAsync();
                cartVM = dbCartItemsToRemove.Select(c => new CartItemVM { VariantId = c.VariantId ?? 0, Quantity = c.Quantity ?? 1 }).ToList();
            }

            if (!cartVM.Any()) return RedirectToAction("Index", "Cart");

            using var transaction = await _context.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable);
            bool transactionCommitted = false;
            int createdOrderId = 0;
            try
            {
                var now = DateTime.Now;
                await RefreshCartMetadataAsync(cartVM, customerId);
                cartVM = cartVM.Where(c => c.Quantity > 0).ToList();
                if (!cartVM.Any()) throw new Exception("Sản phẩm đã hết hàng hoặc không còn đủ điều kiện mua.");

                var activeFlashSale = await _context.FlashSales
                    .Include(f => f.FlashSaleItems)
                    .Where(f => f.IsActive && f.StartTime <= now && f.EndTime >= now)
                    .OrderByDescending(f => f.StartTime)
                    .FirstOrDefaultAsync();

                decimal subtotal = 0;
                var orderDetailsList = new List<OrderDetails>();

                foreach (var item in cartVM)
                {
                    var variant = await _context.ProductVariants.Include(v => v.Product).FirstOrDefaultAsync(v => v.VariantId == item.VariantId && v.IsActive == true);
                    if (variant == null) throw new Exception("Sản phẩm không tồn tại hoặc đã ngừng bán.");

                    int currentStock = variant.Stock ?? 0;
                    if (currentStock < item.Quantity) throw new Exception($"Sản phẩm '{variant.Product?.Name}' chỉ còn {currentStock} sản phẩm.");

                    if (item.IsFlashSale && item.FlashSaleQty > 0)
                    {
                        var fsItem = activeFlashSale?.FlashSaleItems.FirstOrDefault(i => i.VariantId == item.VariantId);
                        if (fsItem == null) throw new Exception($"Suất Flash Sale cho '{variant.Product?.Name}' vừa kết thúc.");

                        int alreadyBought = await GetCustomerFlashSaleBoughtQtyAsync(customerId, fsItem.ItemId);
                        int remainingByCustomerLimit = fsItem.MaxPerUser > 0 ? Math.Max(0, fsItem.MaxPerUser - alreadyBought) : int.MaxValue;
                        int availableFsStock = Math.Max(0, fsItem.Quantity - fsItem.Sold);
                        int allowedFlashSaleQty = Math.Min(item.FlashSaleQty, Math.Min(availableFsStock, remainingByCustomerLimit));

                        if (allowedFlashSaleQty < item.FlashSaleQty)
                        {
                            item.RegularQty += item.FlashSaleQty - allowedFlashSaleQty;
                            item.FlashSaleQty = allowedFlashSaleQty;
                        }

                        if (item.FlashSaleQty > 0)
                        {
                            fsItem.Sold += item.FlashSaleQty;
                            subtotal += item.FlashSalePrice * item.FlashSaleQty;
                            orderDetailsList.Add(new OrderDetails
                            {
                                VariantId = item.VariantId,
                                Quantity = item.FlashSaleQty,
                                UnitPrice = item.FlashSalePrice,
                                IsFlashSaleItem = true,
                                FlashSaleItemId = fsItem.ItemId,
                                IsReviewed = false
                            });
                        }
                    }

                    if (item.RegularQty > 0)
                    {
                        subtotal += item.RegularPrice * item.RegularQty;
                        orderDetailsList.Add(new OrderDetails
                        {
                            VariantId = item.VariantId,
                            Quantity = item.RegularQty,
                            UnitPrice = item.RegularPrice,
                            IsFlashSaleItem = false,
                            IsReviewed = false
                        });
                    }
                }

                decimal discountAmount = 0;
                if (!string.IsNullOrWhiteSpace(AppliedVoucherCode))
                {
                    var voucherStates = await _promotionEngine.EvaluateCartPromotionsAsync(subtotal, customerId);
                    var validVoucher = voucherStates.FirstOrDefault(v => v.Code.Equals(AppliedVoucherCode, StringComparison.OrdinalIgnoreCase) && v.IsEligible);

                    if (validVoucher != null)
                    {
                        discountAmount = validVoucher.EstimatedDiscountAmount;
                        var promotion = await _context.Promotions.FindAsync(validVoucher.PromotionId);
                        if (promotion != null) promotion.UsedCount++;

                        var wallet = await _context.CustomerWallet.FirstOrDefaultAsync(w => w.CustomerId == customerId && w.PromotionId == validVoucher.PromotionId);
                        if (wallet != null) { wallet.Status = 1; wallet.UsedAt = now; }
                    }
                }

                decimal shippingFee = 30000;
                if (!string.IsNullOrEmpty(address.District) && !string.IsNullOrEmpty(address.Ward))
                {
                    try
                    {
                        int districtId = int.Parse(address.District.Split('|')[0]);
                        string wardCode = address.Ward.Split('|')[0];
                        int totalWeight = cartVM.Sum(c => c.Quantity * 500);
                        int insuranceValue = (int)subtotal;
                        shippingFee = await _ghnService.CalculateFeeAsync(districtId, wardCode, totalWeight, insuranceValue);
                    }
                    catch { }
                }

                decimal finalTotal = subtotal - discountAmount + shippingFee;
                if (finalTotal < 0) finalTotal = 0;

                var newOrder = new Orders
                {
                    CustomerId = customerId,
                    OrderDate = now,
                    Status = OrderStatuses.Pending,
                    TotalAmount = finalTotal,
                    ShippingFullName = address.ReceiverName ?? address.Customer?.FullName,
                    ShippingPhone = address.ReceiverPhone ?? address.Customer?.Phone,
                    ShippingStreet = address.Street,
                    ShippingDistrict = address.District,
                    ShippingCity = address.City,
                    ShippingCountry = address.Country ?? "Việt Nam",
                    IsStockDeducted = false,
                    StockDeductedAt = null,
                    OrderDetails = orderDetailsList
                };

                _context.Orders.Add(newOrder);
                await _context.SaveChangesAsync();
                createdOrderId = newOrder.OrderId;

                bool stockDeducted = await _orderInventoryService.DeductOrderStockAsync(
                    newOrder.OrderId,
                    "Trừ kho khi tạo đơn tại checkout",
                    occurredAt: now,
                    cancellationToken: HttpContext.RequestAborted);

                if (!stockDeducted)
                {
                    throw new InvalidOperationException(
                        $"Không thể claim tồn kho cho đơn #{newOrder.OrderId}.");
                }

                string paymentStatus = isCod
                    ? PaymentStatuses.Unpaid
                    : isBankTransfer
                        ? PaymentStatuses.AwaitingBankTransfer
                        : PaymentStatuses.AwaitingGateway;

                string orderCreatedNote = isBankTransfer
                    ? "Đơn hàng mới được hệ thống ghi nhận, đang chờ xác nhận chuyển khoản. Tồn kho đã được giữ cho đơn này."
                    : "Đơn hàng mới được hệ thống ghi nhận, tồn kho đã được giữ cho đơn này.";

                _context.Payments.Add(new Payments { OrderId = newOrder.OrderId, PaymentMethod = PaymentMethod, PaymentDate = now, PaymentStatus = paymentStatus });
                _context.Shipping.Add(new Shipping { OrderId = newOrder.OrderId, Carrier = "Giao hàng tiêu chuẩn", Status = "Chờ lấy hàng" });
                _context.OrderHistories.Add(new OrderHistory { OrderId = newOrder.OrderId, Status = OrderStatuses.Pending, UpdatedAt = now, Note = orderCreatedNote });

                if (dbCartItemsToRemove.Any()) _context.CartItems.RemoveRange(dbCartItemsToRemove);

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();
                transactionCommitted = true;

                if (isVnPay)
                {
                    try
                    {
                        string paymentUrl = _vnPayService.CreatePaymentUrl(HttpContext, newOrder.OrderId, (double)finalTotal);
                        if (string.IsNullOrWhiteSpace(paymentUrl))
                        {
                            throw new InvalidOperationException("Không tạo được đường dẫn thanh toán VNPAY.");
                        }

                        return Redirect(paymentUrl);
                    }
                    catch (Exception paymentEx)
                    {
                        TempData["Error"] = "Đơn hàng đã được ghi nhận, nhưng cổng VNPAY chưa mở được. Bạn có thể thanh toán lại trong chi tiết đơn hàng. Chi tiết: " + paymentEx.Message;
                        return RedirectToAction("OrderPlaced", new { orderId = newOrder.OrderId });
                    }
                }

                TempData["OrderSuccessModal"] = JsonSerializer.Serialize(new { OrderId = newOrder.OrderId, Method = PaymentMethod });
                return RedirectToAction("OrderPlaced", new { orderId = newOrder.OrderId });
            }
            catch (Exception ex)
            {
                string rootError = ex.InnerException?.Message ?? ex.Message;

                if (!transactionCommitted)
                {
                    await transaction.RollbackAsync();
                    TempData["Error"] = "Đơn hàng chưa thể ghi nhận do dữ liệu chưa khớp với cấu trúc hệ thống. Chi tiết kỹ thuật: " + rootError;
                    return RedirectToAction("Index", new { selectedItems, buyNowVariantId, buyNowQty });
                }

                TempData["Error"] = "Đơn hàng đã được ghi nhận nhưng có lỗi khi chuyển bước thanh toán. Vui lòng kiểm tra lại đơn hàng trong tài khoản. Chi tiết kỹ thuật: " + rootError;
                return createdOrderId > 0
                    ? RedirectToAction("OrderPlaced", new { orderId = createdOrderId })
                    : RedirectToAction("Orders", "Customer");
            }
        }

        [HttpGet]
        public async Task<IActionResult> OrderPlaced(int orderId)
        {
            int customerId = int.Parse(User.FindFirstValue("CustomerId") ?? "0");

            var order = await _context.Orders
                .Include(o => o.Payments)
                .Include(o => o.OrderDetails)
                    .ThenInclude(d => d.Variant)
                        .ThenInclude(v => v!.Product)
                .FirstOrDefaultAsync(o => o.OrderId == orderId && o.CustomerId == customerId);

            if (order == null) return RedirectToAction("Orders", "Customer");

            return View(order);
        }

        [HttpGet]
        public async Task<IActionResult> Success(int orderId)
        {
            int customerId = int.Parse(User.FindFirstValue("CustomerId") ?? "0");
            bool isOwnerOrder = await _context.Orders.AnyAsync(o => o.OrderId == orderId && o.CustomerId == customerId);
            if (!isOwnerOrder) return RedirectToAction("Index", "Home");

            TempData["OrderSuccessModal"] = JsonSerializer.Serialize(new { OrderId = orderId, Method = "ORDER" });
            return RedirectToAction("Orders", "Customer");
        }

        private async Task RefreshCartMetadataAsync(List<CartItemVM> cart, int? customerId = null)
        {
            var now = DateTime.Now;
            var activeFlashSale = await _context.FlashSales
                .Include(f => f.FlashSaleItems)
                .Where(f => f.IsActive && f.StartTime <= now && f.EndTime >= now)
                .OrderByDescending(f => f.StartTime)
                .FirstOrDefaultAsync();

            foreach (var item in cart)
            {
                item.IsBundleDiscount = false;
                item.BundleOriginalRegularPrice = 0m;
                item.BundleDiscountAmountPerUnit = 0m;
                item.BundleDiscountLabel = string.Empty;

                var variant = await _context.ProductVariants.FindAsync(item.VariantId);
                if (variant == null)
                {
                    item.Quantity = 0;
                    continue;
                }

                item.ProductId = variant.ProductId;
                item.Stock = variant.Stock ?? 0;
                if (item.Quantity > item.Stock) item.Quantity = item.Stock;
                if (item.Quantity < 0) item.Quantity = 0;

                item.RegularPrice = variant.DiscountPrice > 0 ? variant.DiscountPrice.Value : (variant.Price ?? 0);
                item.RegularQty = item.Quantity;
                item.IsFlashSale = false;
                item.FlashSalePrice = 0;
                item.FlashSaleQty = 0;
                item.MaxPerUser = 0;

                if (item.Quantity == 0 || activeFlashSale == null) continue;

                var fsItem = activeFlashSale.FlashSaleItems.FirstOrDefault(i => i.VariantId == item.VariantId);
                if (fsItem == null || fsItem.Sold >= fsItem.Quantity) continue;

                int availableSaleStock = Math.Max(0, fsItem.Quantity - fsItem.Sold);
                int eligibleSaleQty = Math.Min(item.Quantity, availableSaleStock);

                if (fsItem.MaxPerUser > 0 && customerId.HasValue && customerId.Value > 0)
                {
                    int alreadyBought = await GetCustomerFlashSaleBoughtQtyAsync(customerId.Value, fsItem.ItemId);
                    eligibleSaleQty = Math.Min(eligibleSaleQty, Math.Max(0, fsItem.MaxPerUser - alreadyBought));
                }
                else if (fsItem.MaxPerUser > 0)
                {
                    eligibleSaleQty = Math.Min(eligibleSaleQty, fsItem.MaxPerUser);
                }

                if (eligibleSaleQty <= 0) continue;

                item.IsFlashSale = true;
                item.FlashSalePrice = fsItem.FlashSalePrice;
                item.MaxPerUser = fsItem.MaxPerUser;
                item.FlashSaleQty = eligibleSaleQty;
                item.RegularQty = item.Quantity - eligibleSaleQty;
            }

            await ApplyBundleDiscountsAsync(cart);
        }

        private async Task ApplyBundleDiscountsAsync(List<CartItemVM> cart)
        {
            var productIds = cart.Where(x => x.ProductId > 0 && x.Quantity > 0).Select(x => x.ProductId).Distinct().ToList();
            if (productIds.Count < 2) return;

            var discountMap = await _crossSellAprioriService.GetCartAppliedDiscountsAsync(productIds);
            if (!discountMap.Any()) return;

            foreach (var item in cart.Where(x => x.ProductId > 0 && x.RegularQty > 0))
            {
                if (!discountMap.TryGetValue(item.ProductId, out var discount)) continue;
                if (!discount.IsBundleDiscountApplied || discount.Price <= 0 || discount.Price >= item.RegularPrice) continue;

                item.IsBundleDiscount = true;
                item.BundleOriginalRegularPrice = item.RegularPrice;
                item.BundleDiscountAmountPerUnit = item.RegularPrice - discount.Price;
                item.BundleDiscountLabel = string.IsNullOrWhiteSpace(discount.BundleDiscountLabel) ? "Ưu đãi mua kèm" : discount.BundleDiscountLabel;
                item.RegularPrice = discount.Price;
            }
        }

        private async Task<int> GetCustomerFlashSaleBoughtQtyAsync(int customerId, int flashSaleItemId)
        {
            return await _context.OrderDetails
                .Where(od => od.FlashSaleItemId == flashSaleItemId
                    && od.Order != null
                    && od.Order.CustomerId == customerId
                    && od.Order.Status != OrderStatuses.Cancelled
                    && od.Order.Status != OrderStatuses.Returned)
                .SumAsync(od => (int?)od.Quantity) ?? 0;
        }

        [HttpGet]
        public IActionResult GetGhnProvinces()
        {
            // Endpoint an toàn cho view checkout. Nếu chưa tích hợp danh mục GHN, trả JSON hợp lệ để không làm vỡ trang.
            return Json(new { code = 200, data = Array.Empty<object>(), message = "Danh mục tỉnh/thành GHN chưa được cấu hình." });
        }

        [HttpGet]
        public IActionResult GetGhnDistricts(int provinceId)
        {
            return Json(new { code = 200, data = Array.Empty<object>(), message = "Danh mục quận/huyện GHN chưa được cấu hình." });
        }

        [HttpGet]
        public IActionResult GetGhnWards(int districtId)
        {
            return Json(new { code = 200, data = Array.Empty<object>(), message = "Danh mục phường/xã GHN chưa được cấu hình." });
        }

        [HttpPost]
        public async Task<IActionResult> ValidateVoucher(string code, decimal subtotal)
        {
            int? customerId = null;
            if (User.Identity?.IsAuthenticated == true)
            {
                var rawCustomerId = User.FindFirstValue("CustomerId");
                if (int.TryParse(rawCustomerId, out int parsedCustomerId) && parsedCustomerId > 0)
                {
                    customerId = parsedCustomerId;
                }
            }

            var voucherStates = await _promotionEngine.EvaluateCartPromotionsAsync(subtotal, customerId);
            var targetVoucher = voucherStates.FirstOrDefault(v => v.Code.Equals(code, StringComparison.OrdinalIgnoreCase));
            if (targetVoucher == null) return Json(new { success = false, message = "Mã khuyến mãi không tồn tại." });
            if (!targetVoucher.IsEligible) return Json(new { success = false, message = $"Chưa đủ điều kiện. Mua thêm {targetVoucher.GapAmount:N0}đ để áp dụng." });
            return Json(new { success = true, discountAmount = targetVoucher.EstimatedDiscountAmount, code = targetVoucher.Code });
        }

        [HttpPost]
        public async Task<IActionResult> CalculateShippingFee(int districtId, string wardCode, string? selectedItems, int? buyNowVariantId, int? buyNowQty)
        {
            try
            {
                int customerId = int.Parse(User.FindFirstValue("CustomerId") ?? "0");
                var cartVM = new List<CartItemVM>();

                if (buyNowVariantId.HasValue && buyNowQty.HasValue)
                {
                    cartVM.Add(new CartItemVM { VariantId = buyNowVariantId.Value, Quantity = buyNowQty.Value });
                }
                else
                {
                    var selectedIds = new List<int>();
                    if (!string.IsNullOrWhiteSpace(selectedItems)) selectedIds = selectedItems.Split(',').Where(x => int.TryParse(x, out _)).Select(int.Parse).ToList();
                    var cartItems = await _context.CartItems.Where(c => c.CustomerId == customerId && selectedIds.Contains(c.VariantId ?? 0)).ToListAsync();
                    cartVM = cartItems.Select(c => new CartItemVM { VariantId = c.VariantId ?? 0, Quantity = c.Quantity ?? 1 }).ToList();
                }

                await RefreshCartMetadataAsync(cartVM, customerId);
                int totalWeightInGrams = cartVM.Sum(c => c.Quantity * 500);
                if (totalWeightInGrams == 0) totalWeightInGrams = 500;
                int totalInsuranceValue = (int)cartVM.Sum(c => c.TotalPrice);

                var fee = await _ghnService.CalculateFeeAsync(districtId, wardCode, totalWeightInGrams, totalInsuranceValue);
                return Json(new { success = true, fee = fee, feeFormatted = string.Format("{0:N0}", fee) + " đ" });
            }
            catch (Exception ex)
            {
                return Json(new { success = false, message = ex.Message });
            }
        }

        [HttpPost]
        public async Task<IActionResult> SaveAddressAjax(int addressId, string receiverName, string receiverPhone, string street, string ward, string district, string city, bool isDefault)
        {
            int customerId = int.Parse(User.FindFirstValue("CustomerId") ?? "0");
            Address address;
            if (addressId == 0)
            {
                if (isDefault)
                {
                    var oldDefaults = await _context.Address.Where(a => a.CustomerId == customerId && a.IsDefault == true).ToListAsync();
                    foreach (var item in oldDefaults) item.IsDefault = false;
                }
                address = new Address { CustomerId = customerId, ReceiverName = receiverName, ReceiverPhone = receiverPhone, Street = street, Ward = ward, District = district, City = city, Country = "Việt Nam", IsDefault = isDefault };
                _context.Address.Add(address);
            }
            else
            {
                address = await _context.Address.FirstOrDefaultAsync(a => a.AddressId == addressId && a.CustomerId == customerId);
                if (address == null) return Json(new { success = false, message = "Không tìm thấy địa chỉ." });
                address.ReceiverName = receiverName; address.ReceiverPhone = receiverPhone; address.Street = street; address.Ward = ward; address.District = district; address.City = city;
                if (isDefault) address.IsDefault = true;
            }
            await _context.SaveChangesAsync(); return Json(new { success = true, addressId = address.AddressId });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAddressAjax(int addressId)
        {
            int customerId = int.Parse(User.FindFirstValue("CustomerId") ?? "0");
            var address = await _context.Address.FirstOrDefaultAsync(a => a.AddressId == addressId && a.CustomerId == customerId);
            if (address != null) { _context.Address.Remove(address); await _context.SaveChangesAsync(); return Json(new { success = true }); }
            return Json(new { success = false });
        }
    }
}
