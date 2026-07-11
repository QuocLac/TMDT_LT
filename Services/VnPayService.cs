using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

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

    public sealed record VnPayRefundResult(
        bool Success,
        string RequestId,
        string ResponseCode,
        string TransactionStatus,
        string? ProviderTransactionId,
        string Message,
        string RawResponse);

    public class VnPayService
    {
        private readonly VnPayConfig _config;
        private readonly IHttpClientFactory _httpClientFactory;

        public VnPayService(
            IOptions<VnPayConfig> config,
            IHttpClientFactory httpClientFactory)
        {
            _config = config.Value;
            _httpClientFactory = httpClientFactory;
        }

        public string CreatePaymentUrl(
            HttpContext context,
            int orderId,
            double amount)
        {
            var vnpayData = new SortedList<string, string>(new VnPayCompare());

            string ipAddr = context.Connection.RemoteIpAddress?.ToString()
                ?? "127.0.0.1";
            if (string.IsNullOrEmpty(ipAddr) || ipAddr == "::1")
            {
                ipAddr = "127.0.0.1";
            }

            string secretKey = _config.HashSecret.Trim();
            string tmnCode = _config.TmnId.Trim();
            string returnUrl = _config.ReturnUrl.Trim();

            vnpayData.Add("vnp_Version", "2.1.0");
            vnpayData.Add("vnp_Command", "pay");
            vnpayData.Add("vnp_TmnCode", tmnCode);
            vnpayData.Add(
                "vnp_Amount",
                ((long)(amount * 100)).ToString());
            vnpayData.Add(
                "vnp_CreateDate",
                DateTime.Now.ToString("yyyyMMddHHmmss"));
            vnpayData.Add("vnp_CurrCode", "VND");
            vnpayData.Add("vnp_IpAddr", ipAddr);
            vnpayData.Add("vnp_Locale", "vn");
            vnpayData.Add(
                "vnp_OrderInfo",
                $"ThanhToanDonHang_{orderId}");
            vnpayData.Add("vnp_OrderType", "other");
            vnpayData.Add("vnp_ReturnUrl", returnUrl);
            vnpayData.Add("vnp_TxnRef", orderId.ToString());

            string queryString = BuildQueryString(vnpayData);
            string secureHash = HmacSHA512(secretKey, queryString);

            return $"{_config.PaymentUrl}?{queryString}&vnp_SecureHash={secureHash}";
        }

        public VnPayResponseModel PaymentExecute(
            IQueryCollection collections)
        {
            var vnpayData = new SortedList<string, string>(new VnPayCompare());
            foreach (var (key, value) in collections)
            {
                if (!string.IsNullOrEmpty(key) && key.StartsWith("vnp_"))
                {
                    vnpayData.Add(key, value.ToString());
                }
            }

            string secureHash = collections
                .FirstOrDefault(item => item.Key == "vnp_SecureHash")
                .Value
                .ToString();

            vnpayData.Remove("vnp_SecureHash");
            vnpayData.Remove("vnp_SecureHashType");

            string signData = BuildQueryString(vnpayData);
            string checksum = HmacSHA512(
                _config.HashSecret.Trim(),
                signData);

            return new VnPayResponseModel
            {
                Success = checksum.Equals(
                        secureHash,
                        StringComparison.InvariantCultureIgnoreCase)
                    && collections
                        .FirstOrDefault(item => item.Key == "vnp_ResponseCode")
                        .Value == "00",
                OrderId = collections
                    .FirstOrDefault(item => item.Key == "vnp_TxnRef")
                    .Value
                    .ToString(),
                TransactionId = collections
                    .FirstOrDefault(item => item.Key == "vnp_TransactionNo")
                    .Value
                    .ToString(),
                VnPayResponseCode = collections
                    .FirstOrDefault(item => item.Key == "vnp_ResponseCode")
                    .Value
                    .ToString()
            };
        }

        public VnPayIpnResponse ProcessIPN(
            Dictionary<string, string> parameters)
        {
            var vnpayData = new SortedList<string, string>(new VnPayCompare());
            foreach (var pair in parameters)
            {
                if (!string.IsNullOrEmpty(pair.Key)
                    && pair.Key.StartsWith("vnp_"))
                {
                    vnpayData.Add(pair.Key, pair.Value);
                }
            }

            string secureHash =
                parameters.GetValueOrDefault("vnp_SecureHash") ?? string.Empty;
            vnpayData.Remove("vnp_SecureHash");
            vnpayData.Remove("vnp_SecureHashType");

            string signData = BuildQueryString(vnpayData);
            string checksum = HmacSHA512(
                _config.HashSecret.Trim(),
                signData);

            bool valid = checksum.Equals(
                secureHash,
                StringComparison.InvariantCultureIgnoreCase);
            int.TryParse(
                parameters.GetValueOrDefault("vnp_TxnRef"),
                out int orderId);
            decimal.TryParse(
                parameters.GetValueOrDefault("vnp_Amount"),
                out decimal amount);

            return new VnPayIpnResponse
            {
                IsValidChecksum = valid,
                OrderId = orderId,
                Amount = amount / 100,
                TransactionStatus =
                    parameters.GetValueOrDefault("vnp_TransactionStatus")
                    ?? string.Empty,
                TransactionId =
                    parameters.GetValueOrDefault("vnp_TransactionNo")
                    ?? string.Empty
            };
        }

        public async Task<VnPayRefundResult> RequestRefundAsync(
            int orderId,
            decimal amountToRefund,
            string transactionDate,
            string executedBy,
            string? requestId = null,
            CancellationToken cancellationToken = default)
        {
            string finalRequestId = string.IsNullOrWhiteSpace(requestId)
                ? DateTime.UtcNow.Ticks.ToString()
                : requestId.Trim();

            if (string.IsNullOrWhiteSpace(_config.RefundUrl)
                || string.IsNullOrWhiteSpace(_config.TmnId)
                || string.IsNullOrWhiteSpace(_config.HashSecret))
            {
                return new VnPayRefundResult(
                    false,
                    finalRequestId,
                    "CONFIG_MISSING",
                    "FAILED",
                    null,
                    "Cấu hình hoàn tiền VNPAY chưa đầy đủ.",
                    string.Empty);
            }

            try
            {
                HttpClient client = _httpClientFactory.CreateClient();

                const string version = "2.1.0";
                const string command = "refund";
                const string transactionType = "02";

                string txnRef = orderId.ToString();
                string amount =
                    ((long)(amountToRefund * 100)).ToString();
                string createBy = string.IsNullOrWhiteSpace(executedBy)
                    ? "system"
                    : executedBy.Trim();
                string createDate =
                    DateTime.Now.ToString("yyyyMMddHHmmss");

                string rawData =
                    $"{finalRequestId}|{version}|{command}|"
                    + $"{_config.TmnId.Trim()}|{transactionType}|"
                    + $"{txnRef}|{amount}|{transactionDate}|"
                    + $"{createBy}|{createDate}";

                string secureHash = HmacSHA512(
                    _config.HashSecret.Trim(),
                    rawData);

                var requestBody = new
                {
                    vnp_RequestId = finalRequestId,
                    vnp_Version = version,
                    vnp_Command = command,
                    vnp_TmnId = _config.TmnId.Trim(),
                    vnp_TransactionType = transactionType,
                    vnp_TxnRef = txnRef,
                    vnp_Amount = amount,
                    vnp_TransactionDate = transactionDate,
                    vnp_CreateBy = createBy,
                    vnp_CreateDate = createDate,
                    vnp_SecureHash = secureHash
                };

                using var content = new StringContent(
                    JsonSerializer.Serialize(requestBody),
                    Encoding.UTF8,
                    "application/json");

                using HttpResponseMessage response = await client.PostAsync(
                    _config.RefundUrl,
                    content,
                    cancellationToken);

                string rawResponse = await response.Content
                    .ReadAsStringAsync(cancellationToken);

                if (!response.IsSuccessStatusCode)
                {
                    return new VnPayRefundResult(
                        false,
                        finalRequestId,
                        $"HTTP_{(int)response.StatusCode}",
                        "FAILED",
                        null,
                        "VNPAY trả về lỗi HTTP khi yêu cầu hoàn tiền.",
                        rawResponse);
                }

                using JsonDocument document =
                    JsonDocument.Parse(rawResponse);
                JsonElement root = document.RootElement;

                string responseCode =
                    ReadJsonString(root, "vnp_ResponseCode")
                    ?? string.Empty;
                string transactionStatus =
                    ReadJsonString(root, "vnp_TransactionStatus")
                    ?? responseCode;
                string? providerTransactionId =
                    ReadJsonString(root, "vnp_TransactionNo")
                    ?? ReadJsonString(root, "vnp_TransactionId");
                string message =
                    ReadJsonString(root, "vnp_Message")
                    ?? ReadJsonString(root, "message")
                    ?? (responseCode == "00"
                        ? "VNPAY xác nhận lệnh hoàn tiền."
                        : "VNPAY từ chối lệnh hoàn tiền.");

                return new VnPayRefundResult(
                    responseCode == "00",
                    finalRequestId,
                    responseCode,
                    transactionStatus,
                    providerTransactionId,
                    message,
                    rawResponse);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception ex)
            {
                return new VnPayRefundResult(
                    false,
                    finalRequestId,
                    "EXCEPTION",
                    "FAILED",
                    null,
                    ex.Message,
                    string.Empty);
            }
        }

        // Giữ wrapper cũ để các luồng hủy đơn hiện tại vẫn tương thích.
        public async Task<bool> RequestBankRefundAsync(
            int orderId,
            decimal amountToRefund,
            string transactionDateStr,
            string userExecuted)
        {
            VnPayRefundResult result = await RequestRefundAsync(
                orderId,
                amountToRefund,
                transactionDateStr,
                userExecuted);

            return result.Success;
        }

        private static string? ReadJsonString(
            JsonElement element,
            string propertyName)
        {
            if (!element.TryGetProperty(
                    propertyName,
                    out JsonElement value))
            {
                return null;
            }

            return value.ValueKind switch
            {
                JsonValueKind.String => value.GetString(),
                JsonValueKind.Number
                    or JsonValueKind.True
                    or JsonValueKind.False => value.GetRawText(),
                _ => null
            };
        }

        private string HmacSHA512(
            string key,
            string inputData)
        {
            var hash = new StringBuilder();
            byte[] keyBytes = Encoding.UTF8.GetBytes(key);
            byte[] inputBytes = Encoding.UTF8.GetBytes(inputData);

            using var hmac = new HMACSHA512(keyBytes);
            byte[] hashValue = hmac.ComputeHash(inputBytes);
            foreach (byte currentByte in hashValue)
            {
                hash.Append(currentByte.ToString("x2"));
            }

            return hash.ToString();
        }

        private string BuildQueryString(
            SortedList<string, string> data)
        {
            var builder = new StringBuilder();
            foreach (var pair in data)
            {
                if (!string.IsNullOrEmpty(pair.Value))
                {
                    builder.Append(
                        Uri.EscapeDataString(pair.Key)
                        + "="
                        + Uri.EscapeDataString(pair.Value)
                        + "&");
                }
            }

            if (builder.Length > 0)
            {
                builder.Remove(builder.Length - 1, 1);
            }

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

            int comparison =
                string.Compare(x, y, StringComparison.Ordinal);
            if (comparison != 0)
            {
                return comparison;
            }

            return string.Compare(
                x,
                y,
                StringComparison.Ordinal);
        }
    }
}
