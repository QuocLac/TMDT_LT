using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Threading.RateLimiting;
using TMDT_LT.Data;
using TMDT_LT.Filters;
using TMDT_LT.Services;
using TMDT_LT.Services.Inventory;
using TMDT_LT.Services.AI;

var builder =
    WebApplication.CreateBuilder(args);

// ====================================================================
// 1. Káº¾T Ná»I DATABASE
// ====================================================================
builder.Services.AddScoped<
    CommerceOrderSaveChangesInterceptor>();
builder.Services.AddScoped<
    CheckoutPromotionQuotaInterceptor>();

builder.Services.AddDbContext<
    ApplicationDbContext>(
    (serviceProvider, options) =>
    {
        options.UseSqlServer(
            builder.Configuration
                .GetConnectionString(
                    "DefaultConnection"));

        options.AddInterceptors(
            serviceProvider
                .GetRequiredService<
                    CommerceOrderSaveChangesInterceptor>(),
            serviceProvider
                .GetRequiredService<
                    CheckoutPromotionQuotaInterceptor>());
    });

// ====================================================================
// 2. Cáº¤U HÃŒNH Dá»ŠCH Vá»¤ Cá»T LÃ•I (MVC, HttpContext)
// ====================================================================
builder.Services.AddScoped<
    CheckoutIdempotencyFilter>();
builder.Services.AddScoped<
    ReturnIntakeFilter>();
builder.Services.AddScoped<
    AdminReturnWorkflowFilter>();

builder.Services
    .AddControllersWithViews(
        options =>
        {
            options.Filters
                .AddService<
                    CheckoutIdempotencyFilter>();
            options.Filters
                .AddService<
                    ReturnIntakeFilter>();
            options.Filters
                .AddService<
                    AdminReturnWorkflowFilter>();
        });

builder.Services
    .AddHttpContextAccessor();

// ====================================================================
// 3. Cáº¤U HÃŒNH LÆ¯U TRá»® TRáº NG THÃI (CACHE & SESSION GIá»Ž HÃ€NG)
// ====================================================================
builder.Services
    .AddDistributedMemoryCache();

builder.Services.AddSession(
    options =>
    {
        options.IdleTimeout =
            TimeSpan.FromDays(7);
        options.Cookie.HttpOnly =
            true;
        options.Cookie.IsEssential =
            true;
    });

// ====================================================================
// 4. Cáº¤U HÃŒNH ÄÄ‚NG NHáº¬P & PHÃ‚N QUYá»€N (COOKIE AUTHENTICATION)
// ====================================================================
builder.Services
    .AddAuthentication(
        CookieAuthenticationDefaults
            .AuthenticationScheme)
    .AddCookie(
        options =>
        {
            options.LoginPath =
                "/Auth/Login";
            options.LogoutPath =
                "/Auth/Logout";
            options.AccessDeniedPath =
                "/Home/AccessDenied";
            options.ExpireTimeSpan =
                TimeSpan.FromDays(7);
            options.SlidingExpiration =
                true;
        });

builder.Services.AddAuthorization();

// Giá»›i háº¡n táº§n suáº¥t riÃªng cho AI Ä‘á»ƒ báº£o vá»‡ háº¡n má»©c vÃ  trÃ¡nh spam.
builder.Services.AddRateLimiter(
    options =>
    {
        options.RejectionStatusCode =
            StatusCodes
                .Status429TooManyRequests;

        options.AddPolicy(
            "kingphone-ai-chat",
            httpContext =>
            {
                var customerId =
                    httpContext.User
                        .FindFirst(
                            "CustomerId")
                        ?.Value;

                var remoteIp =
                    httpContext.Connection
                        .RemoteIpAddress
                        ?.ToString()
                    ?? "unknown";

                var partitionKey =
                    string.IsNullOrWhiteSpace(
                        customerId)
                        ? $"guest:{remoteIp}"
                        : $"customer:{customerId}";

                return RateLimitPartition
                    .GetFixedWindowLimiter(
                        partitionKey,
                        _ =>
                            new FixedWindowRateLimiterOptions
                            {
                                PermitLimit = 10,
                                Window =
                                    TimeSpan
                                        .FromMinutes(
                                            1),
                                QueueLimit = 0,
                                AutoReplenishment =
                                    true
                            });
            });
    });

// ====================================================================
// 5. Cáº¤U HÃŒNH SIGNALR (CHAT TRá»°C TUYáº¾N)
// ====================================================================
builder.Services.AddSignalR();

// ====================================================================
// 6. ÄÄ‚NG KÃ CÃC Dá»ŠCH Vá»¤ TÃ™Y CHá»ˆNH (SERVICES & CONFIG)
// ====================================================================
builder.Services
    .AddScoped<
        GoogleAnalyticsService>();
builder.Services
    .AddScoped<
        PromotionEngine>();

builder.Services.AddScoped<
    IOrderInventoryService,
    OrderInventoryService>();

builder.Services.AddInventoryModule();

builder.Services.AddScoped<
    IOrderStateService,
    OrderStateService>();

builder.Services.AddScoped<
    IPaymentTransactionService,
    PaymentTransactionService>();

builder.Services.AddScoped<
    RefundSettlementService>();

builder.Services.AddScoped<
    InspectionAwareRefundSettlementService>();

builder.Services.AddScoped<
    IRefundSettlementService>(
    serviceProvider =>
        serviceProvider
            .GetRequiredService<
                InspectionAwareRefundSettlementService>());

builder.Services.AddScoped<
    ICheckoutIdempotencyService,
    CheckoutIdempotencyService>();

builder.Services.AddScoped<
    IReturnIntakeService,
    ReturnIntakeService>();

builder.Services.AddScoped<
    IReturnWorkflowService,
    ReturnWorkflowService>();

builder.Services.AddScoped<
    IReturnWorkflowNotificationService,
    ReturnWorkflowNotificationService>();

builder.Services.AddScoped<
    IReturnInspectionService,
    ReturnInspectionService>();

builder.Services
    .Configure<
        ReturnIntakeOptions>(
        builder.Configuration
            .GetSection(
                ReturnIntakeOptions
                    .SectionName));

builder.Services
    .Configure<
        UnpaidOrderExpirationOptions>(
        builder.Configuration
            .GetSection(
                UnpaidOrderExpirationOptions
                    .SectionName));

builder.Services
    .AddSingleton<
        PaymentExpirationPolicy>();

builder.Services.AddScoped<
    IUnpaidOrderExpirationService,
    UnpaidOrderExpirationService>();

builder.Services
    .AddHostedService<
        UnpaidOrderExpirationWorker>();

builder.Services.AddHttpClient();

builder.Services.AddScoped<
    IKingPhoneAiToolService,
    KingPhoneAiToolService>();

// Chá»‰ Ä‘á»c section AI. User Secrets AI:ApiKey sáº½ ghi Ä‘Ã¨
// giÃ¡ trá»‹ ApiKey rá»—ng trong appsettings.Development.json.
builder.Services
    .AddOptions<
        KingPhoneAiOptions>()
    .Bind(
        builder.Configuration
            .GetSection(
                KingPhoneAiOptions
                    .SectionName));

builder.Services.AddHttpClient<
    IKingPhoneAiService,
    KingPhoneAiService>(
    client =>
    {
        client.Timeout =
            Timeout.InfiniteTimeSpan;
    });

builder.Services
    .Configure<
        VnPayConfig>(
        builder.Configuration
            .GetSection("VNPay"));

builder.Services
    .AddScoped<
        VnPayService>();

builder.Services
    .Configure<
        GhnOptions>(
        builder.Configuration
            .GetSection(
                GhnOptions
                    .SectionName));

builder.Services
    .AddHttpClient<
        GhnService>();

builder.Services.AddScoped<
    IShippingLifecycleService,
    ShippingLifecycleService>();

builder.Services.AddScoped<
    ICrossSellAprioriService,
    CrossSellAprioriService>();

var app = builder.Build();

// Ghi tráº¡ng thÃ¡i cáº¥u hÃ¬nh, tuyá»‡t Ä‘á»‘i khÃ´ng ghi API key.
using (var scope =
       app.Services.CreateScope())
{
    var aiService =
        scope.ServiceProvider
            .GetRequiredService<
                IKingPhoneAiService>();

    var aiStatus =
        aiService
            .GetConfigurationStatus();

    app.Logger.LogInformation(
        "KingPhone AI configuration: Enabled={Enabled}; Configured={Configured}; Provider={Provider}; Model={Model}; ApiKeyConfigured={ApiKeyConfigured}; ApiKeySource={ApiKeySource}",
        aiStatus.Enabled,
        aiStatus.IsConfigured,
        aiStatus.Provider,
        aiStatus.Model,
        aiStatus.ApiKeyConfigured,
        aiStatus.ApiKeySource);

    if (aiStatus
        .LegacyOpenAiConfigDetected)
    {
        app.Logger.LogWarning(
            "Legacy OpenAI:* configuration is still present. KingPhone AI ignores it; remove the old User Secrets.");
    }
}

// ====================================================================
// MIDDLEWARE PIPELINE
// ====================================================================
if (!app.Environment
        .IsDevelopment())
{
    app.UseExceptionHandler(
        "/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.MapStaticAssets();

app.UseRouting();

app.UseSession();
app.UseAuthentication();
app.UseRateLimiter();
app.UseAuthorization();

app.MapControllerRoute(
    name: "areas",
    pattern:
        "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern:
        "{controller=Home}/{action=Index}/{id?}");

app.Run();

