using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.EntityFrameworkCore;
using TMDT_LT.Data;
using TMDT_LT.Services;

var builder = WebApplication.CreateBuilder(args);

// ====================================================================
// 1. KẾT NỐI DATABASE
// ====================================================================
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// ====================================================================
// 2. CẤU HÌNH DỊCH VỤ CỐT LÕI (MVC, HttpContext)
// ====================================================================
builder.Services.AddControllersWithViews();
builder.Services.AddHttpContextAccessor();

// ====================================================================
// 3. CẤU HÌNH LƯU TRỮ TRẠNG THÁI (CACHE & SESSION GIỎ HÀNG)
// ====================================================================
builder.Services.AddDistributedMemoryCache(); // Khởi tạo bộ nhớ đệm phân tán
builder.Services.AddSession(options =>
{
    // Hợp nhất: Cấu hình giỏ hàng sống 7 ngày (tối ưu trải nghiệm mua sắm)
    options.IdleTimeout = TimeSpan.FromDays(7);
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

// ====================================================================
// 4. CẤU HÌNH ĐĂNG NHẬP & PHÂN QUYỀN (COOKIE AUTHENTICATION)
// ====================================================================
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(options =>
    {
        options.LoginPath = "/Auth/Login";           // Trỏ về trang Đăng nhập
        options.LogoutPath = "/Auth/Logout";         // Trỏ về API Đăng xuất
        options.AccessDeniedPath = "/Home/AccessDenied"; // Khi Admin vào nhầm hoặc Khách vào nhầm Admin
        options.ExpireTimeSpan = TimeSpan.FromDays(7);   // Duy trì đăng nhập 7 ngày
        options.SlidingExpiration = true;            // Tự động gia hạn Cookie nếu user có tương tác
    });

// ====================================================================
// 5. CẤU HÌNH SIGNALR (CHAT TRỰC TUYẾN)
// ====================================================================
builder.Services.AddSignalR();

// ====================================================================
// 6. ĐĂNG KÝ CÁC DỊCH VỤ TÙY CHỈNH (SERVICES & CONFIG)
// ====================================================================
builder.Services.AddScoped<GoogleAnalyticsService>();
builder.Services.AddScoped<PromotionEngine>();

var app = builder.Build();

// ====================================================================
// MIDDLEWARE PIPELINE (THỨ TỰ Ở ĐÂY CỰC KỲ QUAN TRỌNG)
// ====================================================================
if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.MapStaticAssets();

app.UseRouting();

// BỘ BA BẮT BUỘC: SESSION -> AUTHENTICATION -> AUTHORIZATION
app.UseSession();        // 1. Phục hồi Session (để lấy dữ liệu Giỏ hàng)
app.UseAuthentication(); // 2. Xác thực danh tính qua Cookie (User là ai?)
app.UseAuthorization();  // 3. Phân quyền (User có được vào đây không?)

// CẤU HÌNH ENDPOINTS

app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Dashboard}/{action=Index}/{id?}"); // Sửa từ Product sang Dashboard để vào trang Admin chuẩn hơn

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}"); // Mặt tiền hướng khách hàng

app.Run();