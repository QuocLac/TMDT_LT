using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace TMDT_LT.Services
{
    public class GhnService
    {
        private readonly HttpClient _httpClient;
        private readonly IConfiguration _config;
        private readonly string _token;
        private readonly string _shopId;
        private readonly string _baseUrl;

        public GhnService(HttpClient httpClient, IConfiguration config)
        {
            _httpClient = httpClient;
            _config = config;

            // Lấy cấu hình từ appsettings.json
            _token = _config["ShippingAPI:GHN:Token"] ?? "";
            _shopId = _config["ShippingAPI:GHN:ShopId"] ?? "";
            _baseUrl = _config["ShippingAPI:GHN:BaseUrl"] ?? "";

            // Gắn sẵn Headers bảo mật bắt buộc của GHN cho mọi Request
            _httpClient.DefaultRequestHeaders.Add("Token", _token);
            if (!string.IsNullOrEmpty(_shopId))
            {
                _httpClient.DefaultRequestHeaders.Add("ShopId", _shopId);
            }
        }

        // =================================================================
        // 1. TÍNH PHÍ VẬN CHUYỂN (ĐÃ FIX: BỔ SUNG QUẬN GỬI & KÍCH THƯỚC HỘP)
        // =================================================================
        public async Task<decimal> CalculateFeeAsync(int toDistrictId, string toWardCode, int weightInGrams, int insuranceValue)
        {
            var requestData = new
            {
                service_type_id = 2, // Dịch vụ tiêu chuẩn
                from_district_id = 1442, // Quận 1, Hồ Chí Minh (Mã quận kho hàng của bạn để GHN tính khoảng cách tuyến đường)
                to_district_id = toDistrictId,
                to_ward_code = toWardCode,
                weight = weightInGrams,
                length = 20, // Kích thước hộp điện thoại mặc định (cm)
                width = 15,
                height = 10,
                insurance_value = insuranceValue
            };

            var content = new StringContent(JsonSerializer.Serialize(requestData), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/v2/shipping-order/fee", content);

            if (response.IsSuccessStatusCode)
            {
                var responseString = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(responseString);
                var data = doc.RootElement.GetProperty("data");
                return data.GetProperty("total").GetDecimal();
            }

            // Nếu lỗi, quăng Exception ra ngoài để Controller bắt được nguyên nhân thay vì âm thầm trả về 30k
            var errorContent = await response.Content.ReadAsStringAsync();
            throw new Exception($"GHN Fee Error: {errorContent}");
        }

        // =================================================================
        // 2. TẠO MÃ VẬN ĐƠN (CREATE ORDER)
        // =================================================================
        public async Task<string> CreateShippingOrderAsync(
            string toName, string toPhone, string toAddress, string toWardCode, int toDistrictId,
            int totalWeight, int codAmount, int insuranceValue, List<object> items)
        {
            var requestData = new
            {
                payment_type_id = 1, // 1: Người gửi trả cước (Vì mình đã thu phí ship của khách qua bill Web rồi)
                note = "Giao hàng cẩn thận, hàng công nghệ giá trị cao.",
                required_note = "CHOXEMHANGKHONGTHU", // Cho xem hàng nhưng không cho mặc/thử
                to_name = toName,
                to_phone = toPhone,
                to_address = toAddress,
                to_ward_code = toWardCode,
                to_district_id = toDistrictId,
                cod_amount = codAmount, // Thu hộ (Nếu khách đã trả VNPay thì biến này sẽ truyền vào = 0)
                weight = totalWeight,
                length = 20, // Kích thước hộp điện thoại mặc định (cm)
                width = 15,
                height = 10,
                insurance_value = insuranceValue,
                service_type_id = 2,
                items = items // Danh sách máy điện thoại
            };

            var content = new StringContent(JsonSerializer.Serialize(requestData), Encoding.UTF8, "application/json");
            var response = await _httpClient.PostAsync($"{_baseUrl}/v2/shipping-order/create", content);
            var responseString = await response.Content.ReadAsStringAsync();

            if (response.IsSuccessStatusCode)
            {
                using var doc = JsonDocument.Parse(responseString);
                var data = doc.RootElement.GetProperty("data");
                return data.GetProperty("order_code").GetString() ?? ""; // TRẢ VỀ MÃ VẬN ĐƠN CỦA GHN (VD: GHN123456)
            }

            throw new Exception($"Không thể khởi tạo vận đơn GHN: {responseString}");
        }

        // =================================================================
        // 3. PROXY LOAD DỮ LIỆU ĐỊA CHỈ (MASTER DATA - GHN v2 SPECIFICATION)
        // Đã sửa đổi sang POST và bọc JSON theo đúng tài liệu kỹ thuật GHN
        // =================================================================
        public async Task<string> GetProvincesAsync()
        {
            // Tỉnh thành dùng GET tiêu chuẩn
            var response = await _httpClient.GetAsync($"{_baseUrl}/master-data/province");
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetDistrictsAsync(int provinceId)
        {
            // Quận huyện bắt buộc phải POST kèm body JSON {"province_id": id}
            var requestData = new { province_id = provinceId };
            var content = new StringContent(JsonSerializer.Serialize(requestData), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/master-data/district", content);
            return await response.Content.ReadAsStringAsync();
        }

        public async Task<string> GetWardsAsync(int districtId)
        {
            // Phường xã bắt buộc phải POST kèm body JSON {"district_id": id}
            var requestData = new { district_id = districtId };
            var content = new StringContent(JsonSerializer.Serialize(requestData), Encoding.UTF8, "application/json");

            var response = await _httpClient.PostAsync($"{_baseUrl}/master-data/ward", content);
            return await response.Content.ReadAsStringAsync();
        }
    }
}