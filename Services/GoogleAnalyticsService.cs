using Google.Analytics.Data.V1Beta;
using Microsoft.Identity.Client;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace TMDT_LT.Services
{
    public class GoogleAnalyticsService
    {
        // Nhớ thay bằng Property ID thực tế của bạn nhé
        private readonly string _propertyId = "NHẬP_PROPERTY_ID_CỦA_BẠN_VÀO_ĐÂY";
        private readonly string _credentialsPath = "ga4-credentials.json";

        public async Task<Dictionary<string, int>> GetDailyPageViewsAsync(DateTime startDate, DateTime endDate)
        {
            var result = new Dictionary<string, int>();

            try
            {
                string basePath = AppContext.BaseDirectory;
                string fullCredentialsPath = System.IO.Path.Combine(basePath, "..\\..\\..\\", _credentialsPath);
                Environment.SetEnvironmentVariable("GOOGLE_APPLICATION_CREDENTIALS", fullCredentialsPath);

                var client = await BetaAnalyticsDataClient.CreateAsync();

                var request = new RunReportRequest
                {
                    Property = $"properties/{_propertyId}",
                    Dimensions = { new Dimension { Name = "date" } },
                    Metrics = { new Metric { Name = "screenPageViews" } },
                    DateRanges = { new DateRange {
                        StartDate = startDate.ToString("yyyy-MM-dd"),
                        EndDate = endDate.ToString("yyyy-MM-dd")
                    } }
                };

                var response = await client.RunReportAsync(request);

                foreach (var row in response.Rows)
                {
                    string dateStr = row.DimensionValues[0].Value;
                    int views = int.Parse(row.MetricValues[0].Value);
                    result.Add(dateStr, views);
                }
            }
            catch
            {
                // Bỏ qua lỗi nếu mất mạng để không sập web
            }

            return result;
        }
    }
}