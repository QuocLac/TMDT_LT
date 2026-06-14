using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Security.Claims;
using System.Text.Json;
using TMDT_LT.Data;
using TMDT_LT.Models;
using TMDT_LT.Models.ViewModels.Storefront;
using TMDT_LT.Services;

namespace TMDT_LT.Controllers
{
    // Bắt buộc đăng nhập mới được vào trang Checkout
    [Authorize]
    public class CheckoutController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly PromotionEngine _promotionEngine;
        // Giả sử bạn có VnPayService đã đăng ký ở Program.cs
        private readonly VnPayService _vnPayService;

        private const string CART_SESSION_KEY = "PhoneStCartSession";

        public CheckoutController(ApplicationDbContext context, PromotionEngine promotionEngine, VnPayService vnPayService)
        {
            _context = context;
            _promotionEngine = promotionEngine;
            _vnPayService = vnPayService;
        }

        // =================================================================
        // 1. TRANG THANH TOÁN (GET)
        // =================================================================
        [HttpGet]
        public async Task<IActionResult> Index()
        {
            var cart = GetCartFromSession();
            if (!cart.Any()) return RedirectToAction("Index", "Cart"); // Giỏ trống thì đuổi về trang giỏ hàng

            // Lấy ID khách hàng từ Claims (Cookie Đăng nhập)
            int customerId = int.Parse(User.FindFirstValue("CustomerId") ?? "0");
            string email = User.FindFirstValue(ClaimTypes.Email) ?? "";

            // Truy xuất thông tin khách hàng và sổ địa chỉ
            var customer = await _context.Customer
                .Include(c => c.Address)
                .FirstOrDefaultAsync(c => c.CustomerId == customerId);

            if (customer == null) return RedirectToAction("Login", "Auth");

            // Xử lý đối soát Tiền & Voucher
            decimal subtotal = cart.Sum(x => x.TotalPrice);
            var vouchers = await _promotionEngine.EvaluateCartPromotionsAsync(subtotal);
            var bestVoucher = vouchers.FirstOrDefault(v => v.IsEligible);
            decimal discount = bestVoucher?.EstimatedDiscountAmount ?? 0;

            // Xử lý Vận chuyển
            var carriers = await _context.ShippingCarriers.Where(c => c.IsActive).ToListAsync();

            // Giả lập Phí ship mặc định (Bạn có thể gọi API GHN ở đây dựa trên địa chỉ Mặc định của khách)
            decimal defaultShippingFee = 30000;

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

        // =================================================================
        // 2. XỬ LÝ ĐẶT HÀNG (POST)
        // =================================================================
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> PlaceOrder(
            int addressId, string? newFullName, string? newPhone, string? newStreet, string? newWard, string? newDistrict, string? newProvince,
            string paymentMethod, string? note)
        {
            var cart = GetCartFromSession();
            if (!cart.Any()) return RedirectToAction("Index", "Store");

            int customerId = int.Parse(User.FindFirstValue("CustomerId") ?? "0");

            // MỞ TRANSACTION: Đảm bảo nếu trừ tiền/tồn kho lỗi thì Rollback toàn bộ
            using var transaction = await _context.Database.BeginTransactionAsync();
            try
            {
                // 1. XÁC ĐỊNH ĐỊA CHỈ GIAO HÀNG
                string shipName, shipPhone, shipStreet, shipDistrict, shipCity;

                if (addressId == 0) // Trường hợp khách chọn "Thêm địa chỉ mới"
                {
                    shipName = newFullName ?? "";
                    shipPhone = newPhone ?? "";
                    shipStreet = $"{newStreet}, {newWard}";
                    shipDistrict = newDistrict ?? "";
                    shipCity = newProvince ?? "";

                    // Tự động lưu địa chỉ mới vào sổ địa chỉ cho khách
                    var newAddress = new Address
                    {
                        CustomerId = customerId,
                        Street = shipStreet,
                        District = shipDistrict,
                        City = shipCity,
                        IsDefault = false // Có thể cho khách tích chọn làm mặc định trên UI
                    };
                    _context.Address.Add(newAddress);
                }
                else // Khách chọn địa chỉ cũ
                {
                    var existingAddress = await _context.Address.FirstOrDefaultAsync(a => a.AddressId == addressId && a.CustomerId == customerId);
                    if (existingAddress == null) throw new Exception("Địa chỉ không hợp lệ.");

                    var customer = await _context.Customer.FindAsync(customerId);
                    shipName = customer?.FullName ?? "";
                    shipPhone = customer?.Phone ?? "";
                    shipStreet = existingAddress.Street;
                    shipDistrict = existingAddress.District ?? "";
                    shipCity = existingAddress.City ?? "";
                }

                // 2. ĐỐI SOÁT TỒN KHO TRƯỚC KHI TẠO ĐƠN
                foreach (var item in cart)
                {
                    var variant = await _context.ProductVariants.FindAsync(item.VariantId);
                    if (variant == null || variant.Stock < item.Quantity)
                    {
                        TempData["Error"] = $"Sản phẩm {item.ProductName} ({item.Storage}-{item.Color}) đã hết hàng hoặc không đủ số lượng.";
                        return RedirectToAction("Index");
                    }
                    // KHÓA TỒN KHO TẠM THỜI
                    variant.Stock -= item.Quantity;
                }

                // 3. TÍNH TOÁN DÒNG TIỀN
                decimal subtotal = cart.Sum(x => x.TotalPrice);
                var vouchers = await _promotionEngine.EvaluateCartPromotionsAsync(subtotal);
                decimal discount = vouchers.FirstOrDefault(v => v.IsEligible)?.EstimatedDiscountAmount ?? 0;
                decimal shippingFee = 30000; // Thay bằng API GHN thực tế
                decimal totalAmount = (subtotal - discount) + shippingFee;

                // 4. KHỞI TẠO ĐƠN HÀNG (Orders)
                var order = new Orders
                {
                    CustomerId = customerId,
                    OrderDate = DateTime.Now,
                    Status = "Chờ xác nhận", // Đơn mới luôn chờ xác nhận
                    TotalAmount = totalAmount,
                    ShippingFullName = shipName,
                    ShippingPhone = shipPhone,
                    ShippingStreet = shipStreet,
                    ShippingDistrict = shipDistrict,
                    ShippingCity = shipCity
                };
                _context.Orders.Add(order);
                await _context.SaveChangesAsync(); // Lưu để lấy OrderId

                // 5. LƯU CHI TIẾT ĐƠN HÀNG (OrderDetails)
                var orderDetails = cart.Select(item => new OrderDetails
                {
                    OrderId = order.OrderId,
                    VariantId = item.VariantId,
                    Quantity = item.Quantity,
                    UnitPrice = item.Price
                }).ToList();
                _context.OrderDetails.AddRange(orderDetails);

                // 6. LƯU HỒ SƠ VẬN CHUYỂN (Shipping)
                var shipping = new Shipping
                {
                    OrderId = order.OrderId,
                    Carrier = "GHN", // Lấy từ carrier khách chọn
                    Status = "Chờ lấy hàng",
                    Note = note
                };
                _context.Shipping.Add(shipping);

                // 7. LƯU THÔNG TIN THANH TOÁN (Payments)
                var payment = new Payments
                {
                    OrderId = order.OrderId,
                    PaymentMethod = paymentMethod,
                    PaymentDate = DateTime.Now,
                    PaymentStatus = paymentMethod == "COD" ? "Chưa thanh toán" : "Đang chờ cổng thanh toán"
                };
                _context.Payments.Add(payment);

                // Ghi log lịch sử đơn hàng
                _context.OrderHistories.Add(new OrderHistory
                {
                    OrderId = order.OrderId,
                    Status = "Chờ xác nhận",
                    UpdatedAt = DateTime.Now,
                    Note = "Khách hàng đặt đơn thành công."
                });

                await _context.SaveChangesAsync();
                await transaction.CommitAsync(); // Xác nhận chốt giao dịch

                // Xóa giỏ hàng Session
                HttpContext.Session.Remove(CART_SESSION_KEY);

                // =====================================================
                // 8. ĐIỀU HƯỚNG THEO PHƯƠNG THỨC THANH TOÁN
                // =====================================================
                if (paymentMethod == "VNPAY")
                {
                    // Chuyển hướng sang VNPay. Bạn cần truyền OrderId và TotalAmount vào Service của bạn
                    string vnpayUrl = _vnPayService.CreatePaymentUrl(HttpContext, order.OrderId, (double)totalAmount);
                    return Redirect(vnpayUrl);
                }

                // Nếu là COD, sang trang Cảm ơn
                TempData["OrderSuccessModal"] = JsonSerializer.Serialize(new { OrderId = order.OrderId, Method = "COD" });
                return RedirectToAction("Orders", "Customer");
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                TempData["Error"] = "Có lỗi xảy ra trong quá trình đặt hàng: " + ex.Message;
                return RedirectToAction("Index");
            }
        }

        // =================================================================
        // 3. API TÍNH PHÍ SHIP ĐỘNG (AJAX TỪ GIAO DIỆN)
        // =================================================================
        [HttpPost]
        public IActionResult CalculateShippingFee(string district, string ward, string carrierCode)
        {
            // TẠI ĐÂY BẠN GỌI API CỦA GHN HOẶC GHTK
            // Ví dụ mô phỏng:
            decimal fee = 30000;
            if (district.Contains("Quận 1") || district.Contains("Quận 3")) fee = 15000; // Nội thành
            if (carrierCode == "GHTK") fee -= 5000;

            return Json(new { success = true, fee = fee, feeFormatted = string.Format("{0:N0} đ", fee) });
        }

        // =================================================================
        // 4. TRANG ĐẶT HÀNG THÀNH CÔNG
        // =================================================================
        [HttpGet]
        public IActionResult Success(int orderId)
        {
            ViewBag.OrderId = orderId;
            return View();
        }

        // Hàm hỗ trợ lấy Session
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
                // Nếu set mặc định, gỡ mặc định của các địa chỉ cũ
                var oldDefaults = await _context.Address.Where(a => a.CustomerId == customerId && a.IsDefault == true).ToListAsync();
                foreach (var old in oldDefaults) old.IsDefault = false;
            }

            Address address;
            if (addressId == 0) // THÊM MỚI
            {
                address = new Address
                {
                    CustomerId = customerId,
                    ReceiverName = receiverName,
                    ReceiverPhone = receiverPhone,
                    Street = street,
                    Ward = ward,
                    District = district,
                    City = city,
                    IsDefault = isDefault
                };
                _context.Address.Add(address);
            }
            else // CẬP NHẬT
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