using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using TMDT_LT.Data;
using TMDT_LT.Filters;
using TMDT_LT.Services;

var builder = WebApplication.CreateBuilder(args);

// ====================================================================
// 1. KẾT NỐI DATABASE
// ====================================================================
builder.Services.AddScoped<
    CommerceOrderSaveChangesInterceptor>();
builder.Services.AddScoped<
    CheckoutPromotionQuotaInterceptor>();

builder.Services.AddDbContext<ApplicationDbContext>(
    (serviceProvider, options) =>
    {
        options.UseSqlServer(
            builder.Configuration.GetConnectionString(
                "DefaultConnection"));

        options.AddInterceptors(
            serviceProvider.GetRequiredService<
                CommerceOrderSaveChangesInterceptor>(),
            serviceProvider.GetRequiredService<
                CheckoutPromotionQuotaInterceptor>());
    });

// ====================================================================
// 2. CẤU HÌNH DỊCH VỤ CỐT LÕI (MVC, HttpContext)
// ====================================================================
builder.Services.AddScoped<
    CheckoutIdempotencyFilter>();
builder.Services.AddScoped<
    ReturnIntakeFilter>();
builder.Services.AddControllersWithViews(options =>
{
    options.Filters.AddService<
        CheckoutIdempotencyFilter>();
    options.Filters.AddService<
        ReturnIntakeFilter>();
});
builder.Services.AddHttpContextAccessor();

// ====================================================================
// 3. CẤU HÌNH LƯU TRỮ TRẠNG THÁI (CACHE & SESSION GIỎ HÀNG)
// ====================================================================
builder.Services.AddDistributedMemoryCache();
builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromDays(7);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ====================================================================
// 4. CẤU HÌNH ĐĂNG NHẬP & PHÂN QUYỀN (COOKIE AUTHENTICATION)
// ====================================================================
builder.Services.AddAuthentication(
        CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";
        options.LogoutPath = "/Auth/Logout";
        options.AccessDeniedPath = "/Home/AccessDenied";
        options.ExpireTimeSpan = TimeSpan.FromDays(7);
        options.SlidingExpiration = true;
    });

builder.Services.AddAuthorization();

// ====================================================================
// 5. CẤU HÌNH SIGNALR (CHAT TRỰC TUYẾN)
// ====================================================================
builder.Services.AddSignalR();

// ====================================================================
// 6. ĐĂNG KÝ CÁC DỊCH VỤ TÙY CHỈNH (SERVICES & CONFIG)
// ====================================================================
builder.Services.AddScoped<GoogleAnalyticsService>();
builder.Services.AddScoped<PromotionEngine>();
builder.Services.AddScoped<
    IOrderInventoryService,
    OrderInventoryService>();
builder.Services.AddScoped<
    IOrderStateService,
    OrderStateService>();
builder.Services.AddScoped<
    IPaymentTransactionService,
    PaymentTransactionService>();
builder.Services.AddScoped<
    IRefundSettlementService,
    RefundSettlementService>();
builder.Services.AddScoped<
    ICheckoutIdempotencyService,
    CheckoutIdempotencyService>();
builder.Services.AddScoped<
    IReturnIntakeService,
    ReturnIntakeService>();
builder.Services.Configure<ReturnIntakeOptions>(
    builder.Configuration.GetSection(
        ReturnIntakeOptions.SectionName));

builder.Services.Configure<UnpaidOrderExpirationOptions>(
    builder.Configuration.GetSection(
        UnpaidOrderExpirationOptions.SectionName));
builder.Services.AddSingleton<PaymentExpirationPolicy>();
builder.Services.AddScoped<
    IUnpaidOrderExpirationService,
    UnpaidOrderExpirationService>();
builder.Services.AddHostedService<
    UnpaidOrderExpirationWorker>();

builder.Services.AddHttpClient();

builder.Services.Configure<VnPayConfig>(
    builder.Configuration.GetSection("VNPay"));
builder.Services.AddScoped<VnPayService>();

builder.Services.Configure<GhnOptions>(
    builder.Configuration.GetSection(
        GhnOptions.SectionName));
builder.Services.AddHttpClient<GhnService>();
builder.Services.AddScoped<
    IShippingLifecycleService,
    ShippingLifecycleService>();

builder.Services.AddScoped<
    ICrossSellAprioriService,
    CrossSellAprioriService>();

var app = builder.Build();

// ====================================================================
// MIDDLEWARE PIPELINE
// ====================================================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();
app.MapStaticAssets();

app.UseRouting();

app.UseSession();
app.UseAuthentication();
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
