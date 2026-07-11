using Microsoft.Extensions.Options;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace TMDT_LT.Services;

public sealed class GhnOptions
{
    public const string SectionName = "ShippingAPI:GHN";

    public string Token { get; set; } = string.Empty;

    public string ShopId { get; set; } = string.Empty;

    public string BaseUrl { get; set; } = string.Empty;

    public string WebhookSecret { get; set; } = string.Empty;

    public int FromDistrictId { get; set; } = 1442;

    public int ServiceTypeId { get; set; } = 2;

    public int DefaultLengthCm { get; set; } = 20;

    public int DefaultWidthCm { get; set; } = 15;

    public int DefaultHeightCm { get; set; } = 10;

    public int MaxInsuranceValue { get; set; } = 5_000_000;
}

public sealed record GhnItem(
    string Name,
    string Code,
    int Quantity,
    int Price,
    int Length = 20,
    int Width = 15,
    int Height = 10,
    int Weight = 500);

public sealed record GhnCreateShippingRequest(
    int OrderId,
    string ToName,
    string ToPhone,
    string ToAddress,
    string ToWardCode,
    int ToDistrictId,
    int TotalWeight,
    int CodAmount,
    int InsuranceValue,
    IReadOnlyCollection<GhnItem> Items);

public sealed record GhnCreateShippingResult(
    string OrderCode,
    DateTime? ExpectedDeliveryTime,
    decimal? TotalFee);

public sealed class GhnService
{
    private readonly HttpClient _httpClient;
    private readonly GhnOptions _options;

    public GhnService(HttpClient httpClient, IOptions<GhnOptions> options)
    {
        _httpClient = httpClient;
        _options = options.Value;
    }

    public bool IsConfigured =>
        !string.IsNullOrWhiteSpace(_options.BaseUrl)
        && !string.IsNullOrWhiteSpace(_options.Token)
        && !string.IsNullOrWhiteSpace(_options.ShopId);

    public int ServiceTypeId => _options.ServiceTypeId;

    public int MaxInsuranceValue => _options.MaxInsuranceValue;

    public async Task<decimal> CalculateFeeAsync(
        int toDistrictId,
        string toWardCode,
        int weightInGrams,
        int insuranceValue,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var requestData = new
        {
            service_type_id = _options.ServiceTypeId,
            from_district_id = _options.FromDistrictId,
            to_district_id = toDistrictId,
            to_ward_code = toWardCode,
            weight = Math.Max(1, weightInGrams),
            length = _options.DefaultLengthCm,
            width = _options.DefaultWidthCm,
            height = _options.DefaultHeightCm,
            insurance_value = Math.Clamp(insuranceValue, 0, _options.MaxInsuranceValue)
        };

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/v2/shipping-order/fee",
            requestData,
            includeShopId: true);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        string responseString = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"GHN Fee Error: {responseString}");
        }

        using var document = JsonDocument.Parse(responseString);
        var data = GetRequiredData(document.RootElement);
        return ReadDecimal(data, "total")
            ?? throw new InvalidOperationException("GHN không trả về tổng phí vận chuyển.");
    }

    public async Task<GhnCreateShippingResult> CreateShippingOrderAsync(
        GhnCreateShippingRequest requestData,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        var payload = new
        {
            payment_type_id = 1,
            note = "Giao hàng cẩn thận, hàng công nghệ giá trị cao.",
            required_note = "CHOXEMHANGKHONGTHU",
            client_order_code = $"ORD-{requestData.OrderId}",
            to_name = requestData.ToName,
            to_phone = requestData.ToPhone,
            to_address = requestData.ToAddress,
            to_ward_code = requestData.ToWardCode,
            to_district_id = requestData.ToDistrictId,
            cod_amount = Math.Max(0, requestData.CodAmount),
            weight = Math.Max(1, requestData.TotalWeight),
            length = _options.DefaultLengthCm,
            width = _options.DefaultWidthCm,
            height = _options.DefaultHeightCm,
            insurance_value = Math.Clamp(
                requestData.InsuranceValue,
                0,
                _options.MaxInsuranceValue),
            service_type_id = _options.ServiceTypeId,
            items = requestData.Items.Select(item => new
            {
                name = item.Name,
                code = item.Code,
                quantity = Math.Max(1, item.Quantity),
                price = Math.Max(0, item.Price),
                length = Math.Max(1, item.Length),
                width = Math.Max(1, item.Width),
                height = Math.Max(1, item.Height),
                weight = Math.Max(1, item.Weight)
            }).ToArray()
        };

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/v2/shipping-order/create",
            payload,
            includeShopId: true);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        string responseString = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Không thể khởi tạo vận đơn GHN: {responseString}");
        }

        using var document = JsonDocument.Parse(responseString);
        var data = GetRequiredData(document.RootElement);
        string orderCode = ReadString(data, "order_code") ?? string.Empty;

        if (string.IsNullOrWhiteSpace(orderCode))
        {
            throw new InvalidOperationException("GHN không trả về mã vận đơn.");
        }

        DateTime? expectedDelivery = null;
        string? expectedRaw = ReadString(data, "expected_delivery_time");
        if (DateTimeOffset.TryParse(
                expectedRaw,
                CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeLocal,
                out var expectedOffset))
        {
            expectedDelivery = expectedOffset.LocalDateTime;
        }

        decimal? totalFee = ReadDecimal(data, "total_fee")
            ?? ReadDecimal(data, "fee");

        return new GhnCreateShippingResult(orderCode, expectedDelivery, totalFee);
    }

    // Giữ overload cũ để các call site ngoài batch này không bị vỡ.
    public async Task<string> CreateShippingOrderAsync(
        string toName,
        string toPhone,
        string toAddress,
        string toWardCode,
        int toDistrictId,
        int totalWeight,
        int codAmount,
        int insuranceValue,
        List<object> items)
    {
        EnsureConfigured();

        var payload = new
        {
            payment_type_id = 1,
            note = "Giao hàng cẩn thận, hàng công nghệ giá trị cao.",
            required_note = "CHOXEMHANGKHONGTHU",
            to_name = toName,
            to_phone = toPhone,
            to_address = toAddress,
            to_ward_code = toWardCode,
            to_district_id = toDistrictId,
            cod_amount = codAmount,
            weight = totalWeight,
            length = _options.DefaultLengthCm,
            width = _options.DefaultWidthCm,
            height = _options.DefaultHeightCm,
            insurance_value = Math.Clamp(insuranceValue, 0, _options.MaxInsuranceValue),
            service_type_id = _options.ServiceTypeId,
            items
        };

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/v2/shipping-order/create",
            payload,
            includeShopId: true);
        using var response = await _httpClient.SendAsync(request);
        string responseString = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"Không thể khởi tạo vận đơn GHN: {responseString}");
        }

        using var document = JsonDocument.Parse(responseString);
        return ReadString(GetRequiredData(document.RootElement), "order_code")
            ?? throw new InvalidOperationException("GHN không trả về mã vận đơn.");
    }

    public async Task CancelShippingOrderAsync(
        string orderCode,
        CancellationToken cancellationToken = default)
    {
        EnsureConfigured();

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/v2/switch-status/cancel",
            new { order_codes = new[] { orderCode } },
            includeShopId: true);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        string responseString = await response.Content.ReadAsStringAsync(cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException(
                $"GHN không thể hủy vận đơn {orderCode}: {responseString}");
        }
    }

    public async Task<string> GetProvincesAsync(
        CancellationToken cancellationToken = default)
    {
        EnsureTokenAndBaseUrl();

        using var request = CreateJsonRequest(
            HttpMethod.Get,
            "/master-data/province",
            body: null,
            includeShopId: false);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<string> GetDistrictsAsync(
        int provinceId,
        CancellationToken cancellationToken = default)
    {
        EnsureTokenAndBaseUrl();

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/master-data/district",
            new { province_id = provinceId },
            includeShopId: false);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    public async Task<string> GetWardsAsync(
        int districtId,
        CancellationToken cancellationToken = default)
    {
        EnsureTokenAndBaseUrl();

        using var request = CreateJsonRequest(
            HttpMethod.Post,
            "/master-data/ward",
            new { district_id = districtId },
            includeShopId: false);
        using var response = await _httpClient.SendAsync(request, cancellationToken);
        return await response.Content.ReadAsStringAsync(cancellationToken);
    }

    private HttpRequestMessage CreateJsonRequest(
        HttpMethod method,
        string relativePath,
        object? body,
        bool includeShopId)
    {
        string baseUrl = _options.BaseUrl.TrimEnd('/');
        var request = new HttpRequestMessage(method, $"{baseUrl}{relativePath}");
        request.Headers.TryAddWithoutValidation("Token", _options.Token.Trim());

        if (includeShopId)
        {
            request.Headers.TryAddWithoutValidation("ShopId", _options.ShopId.Trim());
        }

        if (body != null)
        {
            request.Content = new StringContent(
                JsonSerializer.Serialize(body),
                Encoding.UTF8,
                "application/json");
        }

        return request;
    }

    private void EnsureConfigured()
    {
        EnsureTokenAndBaseUrl();

        if (string.IsNullOrWhiteSpace(_options.ShopId))
        {
            throw new InvalidOperationException("ShippingAPI:GHN:ShopId chưa được cấu hình.");
        }
    }

    private void EnsureTokenAndBaseUrl()
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl)
            || string.IsNullOrWhiteSpace(_options.Token))
        {
            throw new InvalidOperationException(
                "ShippingAPI:GHN:BaseUrl hoặc Token chưa được cấu hình.");
        }
    }

    private static JsonElement GetRequiredData(JsonElement root)
    {
        if (!root.TryGetProperty("data", out var data))
        {
            throw new InvalidOperationException("Phản hồi GHN không có trường data.");
        }

        return data;
    }

    private static string? ReadString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        return value.ValueKind switch
        {
            JsonValueKind.String => value.GetString(),
            JsonValueKind.Number => value.GetRawText(),
            _ => null
        };
    }

    private static decimal? ReadDecimal(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var value))
        {
            return null;
        }

        if (value.ValueKind == JsonValueKind.Number
            && value.TryGetDecimal(out decimal numeric))
        {
            return numeric;
        }

        if (value.ValueKind == JsonValueKind.String
            && decimal.TryParse(
                value.GetString(),
                NumberStyles.Number,
                CultureInfo.InvariantCulture,
                out decimal parsed))
        {
            return parsed;
        }

        return null;
    }
}
