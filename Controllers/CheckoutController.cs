using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Linq;
using System.Security.Claims;
using System.Text.Json;
using System.Threading.Tasks;
using System.Collections.Generic;
using System;
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
        private readonly GhnService _ghnService; // TIÊM DỊCH VỤ GHN VÀO

        private const string CART_SESSION_KEY = "PhoneStCartSession";

        public CheckoutController(ApplicationDbContext context, PromotionEngine promotionEngine, VnPayService vnPayService, GhnService ghnService)
        {
            _context = context;
            _promotionEngine = promotionEngine;
            _vnPayService = vnPayService;
            _ghnService = ghnService;
        }

        [HttpGet]
        public async Task<IActionResult> Index(string? selectedItems)
        {
            int customerId = int.Parse(User.FindFirstValue("CustomerId") ?? "0");
            string email = User.FindFirstValue(ClaimTypes.Email) ?? "";
            List<CartItemVM> cart = new List<CartItemVM>();

            if (!string.IsNullOrWhiteSpace(selectedItems))
            {
                var variantIds = selectedItems.Split(',').Select(int.Parse).ToList();
                var dbItems = await _context.CartItems
                    .Include(c => c.Variant)
                    .ThenInclude(v => v.Product)
                    .Where(c => c.CustomerId == customerId && variantIds.Contains((int)c.VariantId))
                    .ToListAsync();

                cart = dbItems.Select(c => {
                    decimal price = c.Variant.DiscountPrice > 0 ? c.Variant.DiscountPrice.Value : (c.Variant.Price ?? 0);
                    return new CartItemVM
                    {
                        VariantId = (int)c.VariantId,
                        ProductName = c.Variant.Product.Name,
                        ImageUrl = c.Variant.ImageUrl ?? c.Variant.Product.MainImage,
                        Storage = c.Variant.Storage ?? "",
                        Color = c.Variant.Color ?? "",
                        Price = price,
                        Quantity = (int)c.Quantity,
                        Stock = c.Variant.Stock ?? 0
                    };
                }).ToList();

                HttpContext.Session.SetString(CART_SESSION_KEY, JsonSerializer.Serialize(cart));
            }
            else
            {
                cart = GetCartFromSession();
            }

            if (!cart.Any()) return RedirectToAction("Index", "Cart");

            var customer = await _context.Customer
                .Include(c => c.Address)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId);

            if (customer == null) return RedirectToAction("Login", "Auth");

            decimal subtotal = cart.Sum(x => x.TotalPrice);
            var vouchers = await _promotionEngine.EvaluateCartPromotionsAsync(subtotal);
            var bestVoucher = vouchers.FirstOrDefault(v => v.IsEligible);
            decimal discount = bestVoucher?.EstimatedDiscountAmount ?? 0;

            var carriers = await _context.ShippingCarriers.Where(c => c.IsActive).ToListAsync();
            decimal defaultShippingFee = 0; // Phí ship ban đầu là 0, đợi khách chọn địa chỉ gọi API GHN

            var vm = new CheckoutVM
            {
                CartItems = cart,
                Subtotal = subtotal,
                DiscountAmount = discount,
                AppliedVoucherCode = bestVoucher?.Code,
                ShippingFee = defaultShippingFee,
                FinalTotal = (subtotal - discount) + defaultShippingFee,
                CustomerName = customer.FullName,
                CustomerPhone = customer.Phone ?? "",
                CustomerEmail = email,
                SavedAddresses = customer.Address.OrderByDescending(a => a.IsDefault).ToList(),
                AvailableCarriers = carriers
            };

            return View(vm);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PlaceOrder(
            int addressId, string? newFullName, string? newPhone, string? newStreet, string? newWard, string? newDistrict, string? newProvince,
            string paymentMethod, string? note)
        {
            var cart = GetCartFromSession();
            if (!cart.Any()) return RedirectToAction("Index", "Store");

            int customerId = int.Parse(User.FindFirstValue("CustomerId") ?? "0");

            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                string shipName, shipPhone, shipStreet, shipWard, shipDistrict, shipCity;

                if (addressId == 0) // Địa chỉ mới
                {
                    shipName = newFullName ?? "";
                    shipPhone = newPhone ?? "";
                    shipStreet = newStreet ?? ""; // Không gộp chung với Ward nữa
                    shipWard = newWard ?? "";
                    shipDistrict = newDistrict ?? "";
                    shipCity = newProvince ?? "";

                    var newAddress = new Address
                    {
                        CustomerId = customerId,
                        Street = shipStreet,
                        Ward = shipWard,
                        District = shipDistrict,
                        City = shipCity,
                        IsDefault = false
                    };
                    _context.Address.Add(newAddress);
                }
                else // Địa chỉ cũ
                {
                    var existingAddress = await _context.Address.FirstOrDefaultAsync(a => a.AddressId == addressId && a.CustomerId == customerId);
                    if (existingAddress == null) throw new Exception("Địa chỉ không hợp lệ.");

                    var customer = await _context.Customer.FindAsync(customerId);
                    shipName = customer?.FullName ?? "";
                    shipPhone = customer?.Phone ?? "";
                    shipStreet = existingAddress.Street ?? "";
                    shipWard = existingAddress.Ward ?? "";
                    shipDistrict = existingAddress.District ?? "";
                    shipCity = existingAddress.City ?? "";
                }

                foreach (var item in cart)
                {
                    var variant = await _context.ProductVariants.FindAsync(item.VariantId);
                    if (variant == null || variant.Stock < item.Quantity)
                    {
                        TempData["Error"] = $"Sản phẩm {item.ProductName} ({item.Storage}-{item.Color}) đã hết hàng hoặc không đủ số lượng.";
                        return RedirectToAction("Index");
                    }
                    variant.Stock -= item.Quantity;
                }

                decimal subtotal = cart.Sum(x => x.TotalPrice);
                var vouchers = await _promotionEngine.EvaluateCartPromotionsAsync(subtotal);
                decimal discount = vouchers.FirstOrDefault(v => v.IsEligible)?.EstimatedDiscountAmount ?? 0;

                // Gọi API tính lại phí ship lần cuối trước khi chốt đơn đề phòng khách F12 sửa Code HTML
                int totalWeight = cart.Sum(c => c.Quantity) * 500;
                int insuranceValue = (int)subtotal > 5000000 ? 5000000 : (int)subtotal;
                string dIdStr = shipDistrict.Contains("|") ? shipDistrict.Split('|')[0] : "0";
                string wCodeStr = shipWard.Contains("|") ? shipWard.Split('|')[0] : "0";
                int dId = int.Parse(dIdStr);

                decimal shippingFee = await _ghnService.CalculateFeeAsync(dId, wCodeStr, totalWeight, insuranceValue);
                decimal totalAmount = (subtotal - discount) + shippingFee;

                var order = new Orders
                {
                    CustomerId = customerId,
                    OrderDate = DateTime.Now,
                    Status = "Chờ xác nhận",
                    TotalAmount = totalAmount,
                    ShippingFullName = shipName,
                    ShippingPhone = shipPhone,
                    ShippingStreet = shipStreet,
                    ShippingDistrict = shipDistrict.Contains("|") ? shipDistrict.Split('|')[1] : shipDistrict, // Lưu tên Quận vào DB
                    ShippingCity = shipCity.Contains("|") ? shipCity.Split('|')[1] : shipCity // Lưu tên Tỉnh vào DB
                };
                _context.Orders.Add(order);
                await _context.SaveChangesAsync();

                var orderDetails = cart.Select(item => new OrderDetails
                {
                    OrderId = order.OrderId,
                    VariantId = item.VariantId,
                    Quantity = item.Quantity,
                    UnitPrice = item.Price
                }).ToList();
                _context.OrderDetails.AddRange(orderDetails);

                // LƯU TRỮ ID GHN VÀO TRƯỜNG TRACKING NUMBER CHỜ ADMIN SỬ DỤNG
                var shipping = new Shipping
                {
                    OrderId = order.OrderId,
                    Carrier = "GHN",
                    Status = "Chờ lấy hàng",
                    TrackingNumber = $"{dIdStr}_{wCodeStr}", // Mẹo: Lưu tạm ID Tỉnh Quận tại đây
                    Note = note
                };
                _context.Shipping.Add(shipping);

                var payment = new Payments
                {
                    OrderId = order.OrderId,
                    PaymentMethod = paymentMethod,
                    PaymentDate = DateTime.Now,
                    PaymentStatus = paymentMethod == "COD" ? "Chưa thanh toán" : "Đang chờ cổng thanh toán"
                };
                _context.Payments.Add(payment);

                var purchasedVariantIds = cart.Select(x => x.VariantId).ToList();
                var cartItemsToRemove = await _context.CartItems
                    .Where(c => c.CustomerId == customerId && purchasedVariantIds.Contains((int)c.VariantId))
                    .ToListAsync();

                _context.CartItems.RemoveRange(cartItemsToRemove);

                _context.OrderHistories.Add(new OrderHistory
                {
                    OrderId = order.OrderId,
                    Status = "Chờ xác nhận",
                    UpdatedAt = DateTime.Now,
                    Note = "Khách hàng đặt đơn thành công."
                });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                HttpContext.Session.Remove(CART_SESSION_KEY);

                if (paymentMethod == "VNPAY")
                {
                    string vnpayUrl = _vnPayService.CreatePaymentUrl(HttpContext, order.OrderId, (double)totalAmount);
                    return Redirect(vnpayUrl);
                }

                TempData["OrderSuccessModal"] = JsonSerializer.Serialize(new { OrderId = order.OrderId, Method = "COD" });
                return RedirectToAction("Orders", "Customer");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["Error"] = "Có lỗi xảy ra: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // =================================================================
        // 3. API TÍNH PHÍ SHIP ĐỘNG (GỌI THẲNG QUA GIAO HÀNG NHANH)
        // =================================================================
        [HttpPost]
        public async Task<IActionResult> CalculateShippingFee(int districtId, string wardCode)
        {
            try
            {
                var cart = GetCartFromSession();
                int totalWeight = cart.Sum(c => c.Quantity) * 500;
                int insuranceValue = (int)cart.Sum(c => c.TotalPrice);
                if (insuranceValue > 5000000) insuranceValue = 5000000;

                decimal fee = await _ghnService.CalculateFeeAsync(districtId, wardCode, totalWeight, insuranceValue);

                return Json(new { success = true, fee = fee, feeFormatted = string.Format("{0:N0} đ", fee) });
            }
            catch (Exception ex)
            {
                // Trả ra success = false kèm thông điệp lỗi để Client biết chính xác API bị gì
                return Json(new { success = false, message = ex.Message, fee = 30000, feeFormatted = "30.000 đ (Tạm tính)" });
            }
        }

        // =================================================================
        // 4. BỘ PROXY API: TRUNG CHUYỂN DATA TỪ GHN VỀ FRONTEND (ẨN TOKEN)
        // =================================================================
        [HttpGet]
        public async Task<IActionResult> GetGhnProvinces()
        {
            var data = await _ghnService.GetProvincesAsync();
            return Content(data, "application/json");
        }

        [HttpGet]
        public async Task<IActionResult> GetGhnDistricts(int provinceId)
        {
            var data = await _ghnService.GetDistrictsAsync(provinceId);
            return Content(data, "application/json");
        }

        [HttpGet]
        public async Task<IActionResult> GetGhnWards(int districtId)
        {
            var data = await _ghnService.GetWardsAsync(districtId);
            return Content(data, "application/json");
        }

        [HttpGet]
        public IActionResult Success(int orderId)
        {
            ViewBag.OrderId = orderId;
            return View();
        }

        private List<CartItemVM> GetCartFromSession()
        {
            var json = HttpContext.Session.GetString(CART_SESSION_KEY);
            return json == null ? new List<CartItemVM>() : JsonSerializer.Deserialize<List<CartItemVM>>(json) ?? new List<CartItemVM>();
        }

        // ----------------------------------------------------------------
        // CÁC API AJAX CHO MODAL QUẢN LÝ ĐỊA CHỈ (CRUD)
        // ----------------------------------------------------------------
        [HttpPost]
        public async Task<IActionResult> SaveAddressAjax(int addressId, string receiverName, string receiverPhone, string street, string ward, string district, string city, bool isDefault)
        {
            int customerId = int.Parse(User.FindFirstValue("CustomerId") ?? "0");

            if (isDefault)
            {
                var oldDefaults = await _context.Address.Where(a => a.CustomerId == customerId && a.IsDefault == true).ToListAsync();
                foreach (var old in oldDefaults) old.IsDefault = false;
            }

            Address address;
            if (addressId == 0)
            {
                address = new Address
                {
                    CustomerId = customerId,
                    ReceiverName = receiverName,
                    ReceiverPhone = receiverPhone,
                    Street = street,
                    Ward = ward,       // Lưu luôn mã nối "20101|Phường Bến Nghé"
                    District = district, // Lưu luôn "1442|Quận 1"
                    City = city,         // Lưu luôn "202|Hồ Chí Minh"
                    IsDefault = isDefault
                };
                _context.Address.Add(address);
            }
            else
            {
                address = await _context.Address.FirstOrDefaultAsync(a => a.AddressId == addressId && a.CustomerId == customerId);
                if (address == null) return Json(new { success = false, message = "Không tìm thấy địa chỉ." });

                address.ReceiverName = receiverName;
                address.ReceiverPhone = receiverPhone;
                address.Street = street;
                address.Ward = ward;
                address.District = district;
                address.City = city;
                if (isDefault) address.IsDefault = true;
            }

            await _context.SaveChangesAsync();
            return Json(new { success = true, addressId = address.AddressId });
        }

        [HttpPost]
        public async Task<IActionResult> DeleteAddressAjax(int addressId)
        {
            int customerId = int.Parse(User.FindFirstValue("CustomerId") ?? "0");
            var address = await _context.Address.FirstOrDefaultAsync(a => a.AddressId == addressId && a.CustomerId == customerId);

            if (address != null)
            {
                _context.Address.Remove(address);
                await _context.SaveChangesAsync();
                return Json(new { success = true });
            }
            return Json(new { success = false });
        }
    }
}