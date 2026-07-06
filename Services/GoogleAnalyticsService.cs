using Google.Apis.AnalyticsData.v1beta;
using Google.Apis.AnalyticsData.v1beta.Data;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace TMDT_LT.Services
{
    public class GoogleAnalyticsConnectionStatus
    {
        public bool IsEnabled { get; set; }
        public bool IsConfigured { get; set; }
        public bool IsConnected { get; set; }
        public string Level { get; set; } = "danger"; // success | warning | danger
        public string Title { get; set; } = "Google Analytics chưa kết nối";
        public string Message { get; set; } = "Chưa kiểm tra kết nối Google Analytics.";
        public string? MeasurementId { get; set; }
        public string? PropertyId { get; set; }
        public string? CredentialsPath { get; set; }
        public bool CredentialsFileExists { get; set; }
        public string? TechnicalDetail { get; set; }
        public DateTime CheckedAt { get; set; } = DateTime.Now;
    }

    public class GoogleAnalyticsService
    {
        private const string AnalyticsReadonlyScope = "https://www.googleapis.com/auth/analytics.readonly";
        private readonly IConfiguration _configuration;
        private readonly IWebHostEnvironment _environment;

        public GoogleAnalyticsService(IConfiguration configuration, IWebHostEnvironment environment)
        {
            _configuration = configuration;
            _environment = environment;
        }

        private bool IsEnabled()
        {
            return string.Equals(_configuration["GoogleAnalytics:Enabled"], "true", StringComparison.OrdinalIgnoreCase);
        }

        private string? GetPropertyId()
        {
            var propertyId = _configuration["GoogleAnalytics:PropertyId"];
            if (string.IsNullOrWhiteSpace(propertyId) || propertyId.Contains("YOUR", StringComparison.OrdinalIgnoreCase)) return null;
            return propertyId.Trim();
        }

        private string? GetMeasurementId()
        {
            var measurementId = _configuration["GoogleAnalytics:MeasurementId"];
            if (string.IsNullOrWhiteSpace(measurementId) || measurementId.Contains("YOUR", StringComparison.OrdinalIgnoreCase)) return null;
            return measurementId.Trim();
        }

        private string? ResolveCredentialsPath()
        {
            var credentialsPath = _configuration["GoogleAnalytics:CredentialsPath"];
            if (string.IsNullOrWhiteSpace(credentialsPath) || credentialsPath.Contains("YOUR", StringComparison.OrdinalIgnoreCase)) return null;

            credentialsPath = credentialsPath.Trim();

            var candidates = new List<string>();

            if (Path.IsPathRooted(credentialsPath))
            {
                candidates.Add(credentialsPath);
            }
            else
            {
                candidates.Add(Path.Combine(_environment.ContentRootPath, credentialsPath));
                candidates.Add(Path.Combine(AppContext.BaseDirectory, credentialsPath));
                candidates.Add(Path.Combine(Directory.GetCurrentDirectory(), credentialsPath));
            }

            var existingPath = candidates.FirstOrDefault(File.Exists);
            return existingPath ?? candidates.FirstOrDefault();
        }

        private static string? TryReadServiceAccountEmail(string credentialsFullPath)
        {
            try
            {
                using var stream = File.OpenRead(credentialsFullPath);
                using var doc = JsonDocument.Parse(stream);
                if (doc.RootElement.TryGetProperty("client_email", out var emailElement))
                {
                    return emailElement.GetString();
                }
            }
            catch
            {
                // Chỉ dùng để hiển thị chẩn đoán, không làm fail kết nối.
            }

            return null;
        }

        private static async Task<GoogleCredential> CreateScopedGoogleCredentialAsync(string credentialsFullPath)
        {
            if (string.IsNullOrWhiteSpace(credentialsFullPath) || !File.Exists(credentialsFullPath))
            {
                throw new FileNotFoundException("Không tìm thấy file service account credentials.", credentialsFullPath);
            }

            var credential = GoogleCredential.FromFile(credentialsFullPath)
                .CreateScoped(AnalyticsReadonlyScope);

            // Ép tạo OAuth2 access token ngay tại đây để bắt lỗi credentials rõ hơn.
            // Nếu bước này pass thì request gửi sang GA Data API chắc chắn đã có token.
            var tokenAccess = credential.UnderlyingCredential as Google.Apis.Auth.OAuth2.ITokenAccess;

            if (tokenAccess == null)
            {
                throw new InvalidOperationException("GoogleCredential đã được tạo nhưng credential bên trong không hỗ trợ sinh OAuth2 access token.");
            }

            var accessToken = await tokenAccess.GetAccessTokenForRequestAsync();

            if (string.IsNullOrWhiteSpace(accessToken))
            {
                throw new InvalidOperationException("GoogleCredential đã được tạo nhưng không sinh được OAuth2 access token.");
            }

            return credential;
        }

        private static async Task<AnalyticsDataService> CreateAuthenticatedServiceAsync(string credentialsFullPath)
        {
            var credential = await CreateScopedGoogleCredentialAsync(credentialsFullPath);

            return new AnalyticsDataService(new BaseClientService.Initializer
            {
                HttpClientInitializer = credential,
                ApplicationName = "KingPhone TMDT Analytics"
            });
        }

        private static string BuildFriendlyTechnicalDetail(Exception ex)
        {
            var message = ex.ToString();

            if (message.Contains("invalid_grant", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Invalid JWT", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("JsonWebSignature", StringComparison.OrdinalIgnoreCase))
            {
                return "Google không tạo được OAuth2 access token từ service account JSON. Hãy tạo lại key JSON mới cho service account, đặt lại file ga4-credentials.json vào root project, rồi kiểm tra lại. Chi tiết gốc: " + ex.Message;
            }

            if (message.Contains("Unauthenticated", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("invalid authentication credentials", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("Expected OAuth 2 access token", StringComparison.OrdinalIgnoreCase))
            {
                return "Google trả về Unauthenticated. Bản v1.4 đã chuyển sang GoogleCredential.FromFile(...).CreateScoped(...) và ép sinh access token trước khi gọi GA Data API. Nếu vẫn gặp lỗi này, hãy tạo lại service account key JSON vì token không được Google chấp nhận. Chi tiết gốc: " + ex.Message;
            }

            if (message.Contains("PermissionDenied", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("does not have sufficient permissions", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("User does not have sufficient permissions", StringComparison.OrdinalIgnoreCase) ||
                message.Contains("403", StringComparison.OrdinalIgnoreCase))
            {
                return "Google trả về PermissionDenied: service account đã xác thực được nhưng chưa có quyền đọc GA4 property. Hãy thêm email service account vào Google Analytics > Admin > Property Access Management với quyền Viewer hoặc Analyst. Chi tiết gốc: " + ex.Message;
            }

            if ((message.Contains("API", StringComparison.OrdinalIgnoreCase) && message.Contains("disabled", StringComparison.OrdinalIgnoreCase)) ||
                message.Contains("has not been used", StringComparison.OrdinalIgnoreCase))
            {
                return "Google Analytics Data API có thể chưa được bật trong Google Cloud project chứa service account. Hãy bật Google Analytics Data API rồi kiểm tra lại. Chi tiết gốc: " + ex.Message;
            }

            if (message.Contains("not found", StringComparison.OrdinalIgnoreCase) || message.Contains("404", StringComparison.OrdinalIgnoreCase))
            {
                return "Google không tìm thấy GA4 property theo Property ID đang cấu hình. Hãy kiểm tra lại Property ID dạng số, không dùng Measurement ID. Chi tiết gốc: " + ex.Message;
            }

            return ex.Message;
        }

        public async Task<GoogleAnalyticsConnectionStatus> CheckConnectionAsync()
        {
            var status = new GoogleAnalyticsConnectionStatus
            {
                IsEnabled = IsEnabled(),
                MeasurementId = GetMeasurementId(),
                PropertyId = GetPropertyId(),
                CredentialsPath = ResolveCredentialsPath(),
                CheckedAt = DateTime.Now
            };

            status.CredentialsFileExists = !string.IsNullOrWhiteSpace(status.CredentialsPath) && File.Exists(status.CredentialsPath);
            var serviceAccountEmail = status.CredentialsFileExists ? TryReadServiceAccountEmail(status.CredentialsPath!) : null;

            if (!status.IsEnabled)
            {
                status.Level = "warning";
                status.Title = "Google Analytics đang tắt";
                status.Message = "Hệ thống vẫn ghi nhận hành vi nội bộ trong database, nhưng chưa gửi/đọc dữ liệu từ Google Analytics. Bật GoogleAnalytics:Enabled = true khi đã có MeasurementId, PropertyId và file credentials.";
                return status;
            }

            if (string.IsNullOrWhiteSpace(status.MeasurementId))
            {
                status.Level = "danger";
                status.Title = "Thiếu GA4 Measurement ID";
                status.Message = "Chưa cấu hình GoogleAnalytics:MeasurementId. Tracking script GA4 ngoài storefront sẽ không được tải.";
                return status;
            }

            if (string.IsNullOrWhiteSpace(status.PropertyId))
            {
                status.Level = "danger";
                status.Title = "Thiếu GA4 Property ID";
                status.Message = "Chưa cấu hình GoogleAnalytics:PropertyId. Admin dashboard không thể đọc báo cáo từ Google Analytics Data API.";
                return status;
            }

            if (string.IsNullOrWhiteSpace(status.CredentialsPath))
            {
                status.Level = "danger";
                status.Title = "Thiếu file credentials Google";
                status.Message = "Chưa cấu hình GoogleAnalytics:CredentialsPath. Cần file JSON của service account để server đọc báo cáo GA4.";
                return status;
            }

            if (!status.CredentialsFileExists)
            {
                status.Level = "danger";
                status.Title = "Không tìm thấy file credentials";
                status.Message = "Đường dẫn credentials đã cấu hình nhưng server không tìm thấy file. Nên đặt file tại root project TMDT_LT hoặc cấu hình CredentialsPath trỏ đúng vị trí.";
                status.TechnicalDetail = "Đường dẫn hệ thống đang kiểm tra: " + status.CredentialsPath;
                return status;
            }

            try
            {
                var service = await CreateAuthenticatedServiceAsync(status.CredentialsPath);
                var request = new RunReportRequest
                {
                    Dimensions = new List<Dimension> { new Dimension { Name = "date" } },
                    Metrics = new List<Metric> { new Metric { Name = "screenPageViews" } },
                    DateRanges = new List<DateRange> { new DateRange { StartDate = "7daysAgo", EndDate = "today" } },
                    Limit = 1
                };

                await service.Properties.RunReport(request, $"properties/{status.PropertyId}").ExecuteAsync();

                status.IsConfigured = true;
                status.IsConnected = true;
                status.Level = "success";
                status.Title = "Google Analytics đã kết nối";
                status.Message = "Server đã tạo được OAuth2 access token từ service account và đọc thử báo cáo GA4 thành công.";
                status.TechnicalDetail = string.IsNullOrWhiteSpace(serviceAccountEmail)
                    ? null
                    : "Service account đang dùng: " + serviceAccountEmail;
                return status;
            }
            catch (Exception ex)
            {
                status.IsConfigured = true;
                status.IsConnected = false;
                status.Level = "danger";
                status.Title = "Chưa kết nối được Google Analytics";
                status.Message = "Cấu hình đã bật nhưng server chưa đọc được dữ liệu từ GA4. Kiểm tra service account, quyền Viewer/Analyst trên GA4 property và Google Analytics Data API.";
                status.TechnicalDetail = (string.IsNullOrWhiteSpace(serviceAccountEmail) ? "" : "Service account đang dùng: " + serviceAccountEmail + " | ") + BuildFriendlyTechnicalDetail(ex);
                return status;
            }
        }

        public async Task<Dictionary<string, int>> GetDailyPageViewsAsync(DateTime startDate, DateTime endDate)
        {
            var result = new Dictionary<string, int>();
            if (!IsEnabled()) return result;

            var propertyId = GetPropertyId();
            var credentialsPath = ResolveCredentialsPath();

            if (string.IsNullOrWhiteSpace(propertyId)) return result;
            if (string.IsNullOrWhiteSpace(credentialsPath) || !File.Exists(credentialsPath)) return result;

            try
            {
                var service = await CreateAuthenticatedServiceAsync(credentialsPath);
                var request = new RunReportRequest
                {
                    Dimensions = new List<Dimension> { new Dimension { Name = "date" } },
                    Metrics = new List<Metric> { new Metric { Name = "screenPageViews" } },
                    DateRanges = new List<DateRange> { new DateRange { StartDate = startDate.ToString("yyyy-MM-dd"), EndDate = endDate.ToString("yyyy-MM-dd") } }
                };

                var response = await service.Properties.RunReport(request, $"properties/{propertyId}").ExecuteAsync();
                foreach (var row in response.Rows ?? new List<Row>())
                {
                    string dateStr = row.DimensionValues[0].Value;
                    if (int.TryParse(row.MetricValues[0].Value, out int views))
                    {
                        result[dateStr] = views;
                    }
                }
            }
            catch
            {
                // Không để lỗi GA làm sập dashboard vận hành. Khi GA chưa cấu hình, hệ thống dùng dữ liệu nội bộ.
            }

            return result;
        }
    }
}
