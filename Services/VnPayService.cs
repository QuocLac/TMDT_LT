using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Net.Http;

namespace TMDT_LT.Services
{
    public class VnPayConfig
    {
        public string TmnId { get; set; } = string.Empty;
        public string HashSecret { get; set; } = string.Empty;
        public string PaymentUrl { get; set; } = string.Empty;
        public string ReturnUrl { get; set; } = string.Empty;
        public string RefundUrl { get; set; } = string.Empty;
    }

    public class VnPayResponseModel
    {
        public bool Success { get; set; }
        public string OrderId { get; set; } = string.Empty;
        public string TransactionId { get; set; } = string.Empty;
        public string VnPayResponseCode { get; set; } = string.Empty;
    }

    public class VnPayIpnResponse
    {
        public bool IsValidChecksum { get; set; }
        public int OrderId { get; set; }
        public decimal Amount { get; set; }
        public string TransactionStatus { get; set; } = string.Empty;
        public string TransactionId { get; set; } = string.Empty;
    }

    public class VnPayService
    {
        private readonly VnPayConfig _config;
        private readonly IHttpClientFactory _httpClientFactory;

        public VnPayService(IOptions<VnPayConfig> config, IHttpClientFactory httpClientFactory)
        {
            _config = config.Value;
            _httpClientFactory = httpClientFactory;
        }

        // HÀM 1: TẠO URL THANH TOÁN
        public string CreatePaymentUrl(HttpContext context, int orderId, double amount)
        {
            var vnpayData = new SortedList<string, string>(new VnPayCompare());

            // FIX 1: Ép IP về chuẩn IPv4 (Tránh lỗi ::1 của localhost)
            string ipAddr = context.Connection.RemoteIpAddress?.ToString() ?? "127.0.0.1";
            if (string.IsNullOrEmpty(ipAddr) || ipAddr == "::1") ipAddr = "127.0.0.1";

            // FIX 2: Tự động Trim() để xóa sạch các khoảng trắng bị dư do Copy-Paste
            string secretKey = _config.HashSecret.Trim();
            string tmnCode = _config.TmnId.Trim();
            string returnUrl = _config.ReturnUrl.Trim();

            vnpayData.Add("vnp_Version", "2.1.0");
            vnpayData.Add("vnp_Command", "pay");
            vnpayData.Add("vnp_TmnCode", tmnCode);
            vnpayData.Add("vnp_Amount", ((long)(amount * 100)).ToString());
            vnpayData.Add("vnp_CreateDate", DateTime.Now.ToString("yyyyMMddHHmmss"));
            vnpayData.Add("vnp_CurrCode", "VND");
            vnpayData.Add("vnp_IpAddr", ipAddr);
            vnpayData.Add("vnp_Locale", "vn");

            // FIX 3: Xóa khoảng trắng trong OrderInfo để tránh lệch chuẩn mã hóa URL (URL Encoding)
            vnpayData.Add("vnp_OrderInfo", $"ThanhToanDonHang_{orderId}");

            vnpayData.Add("vnp_OrderType", "other");
            vnpayData.Add("vnp_ReturnUrl", returnUrl);
            vnpayData.Add("vnp_TxnRef", orderId.ToString());

            string queryString = BuildQueryString(vnpayData);
            string vnp_SecureHash = HmacSHA512(secretKey, queryString);

            return $"{_config.PaymentUrl}?{queryString}&vnp_SecureHash={vnp_SecureHash}";
        }

        // HÀM 2: NHẬN PHẢN HỒI
        public VnPayResponseModel PaymentExecute(IQueryCollection collections)
        {
            var vnpayData = new SortedList<string, string>(new VnPayCompare());
            foreach (var (key, value) in collections)
            {
                if (!string.IsNullOrEmpty(key) && key.StartsWith("vnp_"))
                {
                    vnpayData.Add(key, value.ToString());
                }
            }

            string vnp_SecureHash = collections.FirstOrDefault(k => k.Key == "vnp_SecureHash").Value.ToString();
            vnpayData.Remove("vnp_SecureHash");
            vnpayData.Remove("vnp_SecureHashType");

            string signData = BuildQueryString(vnpayData);
            string checkSum = HmacSHA512(_config.HashSecret.Trim(), signData);

            return new VnPayResponseModel
            {
                Success = checkSum.Equals(vnp_SecureHash, StringComparison.InvariantCultureIgnoreCase) && collections.FirstOrDefault(k => k.Key == "vnp_ResponseCode").Value == "00",
                OrderId = collections.FirstOrDefault(k => k.Key == "vnp_TxnRef").Value.ToString(),
                TransactionId = collections.FirstOrDefault(k => k.Key == "vnp_TransactionNo").Value.ToString(),
                VnPayResponseCode = collections.FirstOrDefault(k => k.Key == "vnp_ResponseCode").Value.ToString()
            };
        }

        // HÀM 3: IPN WEBHOOK
        public VnPayIpnResponse ProcessIPN(Dictionary<string, string> parameters)
        {
            var vnpayData = new SortedList<string, string>(new VnPayCompare());
            foreach (var kv in parameters)
            {
                if (!string.IsNullOrEmpty(kv.Key) && kv.Key.StartsWith("vnp_"))
                {
                    vnpayData.Add(kv.Key, kv.Value);
                }
            }

            string vnp_SecureHash = parameters.GetValueOrDefault("vnp_SecureHash") ?? "";
            vnpayData.Remove("vnp_SecureHash");
            vnpayData.Remove("vnp_SecureHashType");

            string signData = BuildQueryString(vnpayData);
            string checkSum = HmacSHA512(_config.HashSecret.Trim(), signData);

            bool isValid = checkSum.Equals(vnp_SecureHash, StringComparison.InvariantCultureIgnoreCase);
            int.TryParse(parameters.GetValueOrDefault("vnp_TxnRef"), out int orderId);
            decimal.TryParse(parameters.GetValueOrDefault("vnp_Amount"), out decimal amount);

            return new VnPayIpnResponse
            {
                IsValidChecksum = isValid,
                OrderId = orderId,
                Amount = amount / 100,
                TransactionStatus = parameters.GetValueOrDefault("vnp_TransactionStatus") ?? "",
                TransactionId = parameters.GetValueOrDefault("vnp_TransactionNo") ?? ""
            };
        }

        // HÀM 4: REFUND HOÀN TIỀN
        public async Task<bool> RequestBankRefundAsync(int orderId, decimal amountToRefund, string transactionDateStr, string userExecuted)
        {
            try
            {
                var client = _httpClientFactory.CreateClient();

                string vnp_RequestId = DateTime.Now.Ticks.ToString();
                string vnp_Version = "2.1.0";
                string vnp_Command = "refund";
                string vnp_TxnRef = orderId.ToString();
                string vnp_Amount = ((long)(amountToRefund * 100)).ToString();
                string vnp_TransactionType = "02";
                string vnp_CreateBy = userExecuted;
                string vnp_CreateDate = DateTime.Now.ToString("yyyyMMddHHmmss");

                string rawData = $"{vnp_RequestId}|{vnp_Version}|{vnp_Command}|{_config.TmnId.Trim()}|{vnp_TransactionType}|{vnp_TxnRef}|{vnp_Amount}|{transactionDateStr}|{vnp_CreateBy}|{vnp_CreateDate}";
                string vnp_SecureHash = HmacSHA512(_config.HashSecret.Trim(), rawData);

                var requestBody = new
                {
                    vnp_RequestId,
                    vnp_Version,
                    vnp_Command,
                    vnp_TmnId = _config.TmnId.Trim(),
                    vnp_TransactionType,
                    vnp_TxnRef,
                    vnp_Amount,
                    vnp_TransactionDate = transactionDateStr,
                    vnp_CreateBy,
                    vnp_CreateDate,
                    vnp_SecureHash
                };

                var content = new StringContent(JsonSerializer.Serialize(requestBody), Encoding.UTF8, "application/json");
                var response = await client.PostAsync(_config.RefundUrl, content);

                if (response.IsSuccessStatusCode)
                {
                    var responseString = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(responseString);
                    string responseCode = doc.RootElement.GetProperty("vnp_ResponseCode").GetString() ?? "";
                    if (responseCode == "00") return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
        }

        // HÀM HỖ TRỢ
        private string HmacSHA512(string key, string inputData)
        {
            var hash = new StringBuilder();
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] inputBytes = Encoding.UTF8.GetBytes(inputData);
            using (var hmac = new HMACSHA512(keyBytes))
            {
                byte[] hashValue = hmac.ComputeHash(inputBytes);
                foreach (var theByte in hashValue)
                {
                    hash.Append(theByte.ToString("x2"));
                }
            }
            return hash.ToString();
        }

        private string BuildQueryString(SortedList<string, string> data)
        {
            var builder = new StringBuilder();
            foreach (var kv in data)
            {
                if (!string.IsNullOrEmpty(kv.Value))
                {
                    builder.Append(Uri.EscapeDataString(kv.Key) + "=" + Uri.EscapeDataString(kv.Value) + "&");
                }
            }
            if (builder.Length > 0) builder.Remove(builder.Length - 1, 1);
            return builder.ToString();
        }
    }

    public class VnPayCompare : IComparer<string>
    {
        public int Compare(string? x, string? y)
        {
            if (x == y) return 0;
            if (x == null) return -1;
            if (y == null) return 1;
            var vnpCompare = string.Compare(x, y, StringComparison.Ordinal);
            if (vnpCompare != 0) return vnpCompare;
            return string.Compare(x, y, StringComparison.Ordinal);
        }
    }
}