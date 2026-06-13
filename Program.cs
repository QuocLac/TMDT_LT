using Microsoft.EntityFrameworkCore;
using TMDT_LT.Data;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddScoped<TMDT_LT.Services.GoogleAnalyticsService>();

// 1. CHUYỂN ĐỔI SANG DỊCH VỤ MVC
builder.Services.AddControllersWithViews();

// Giữ nguyên cấu hình kết nối DB từ bước trước
builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Đăng ký dịch vụ lưu trữ Session trên bộ nhớ RAM
builder.Services.AddDistributedMemoryCache();

builder.Services.AddSession(options =>
{
    options.IdleTimeout = TimeSpan.FromMinutes(60); // Thời gian sống của Giỏ hàng (60 phút)
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddDistributedMemoryCache(); // Khởi tạo bộ nhớ đệm phân tán ngầm
builder.Services.AddSession(options => {
    options.IdleTimeout = TimeSpan.FromMinutes(30); // Giỏ hàng tự hủy sau 30 phút rời trang
    options.Cookie.HttpOnly = true;
    options.Cookie.IsEssential = true;
});

builder.Services.AddHttpContextAccessor();

// Đăng ký dịch vụ PromotionEngine cho Giỏ hàng
builder.Services.AddScoped<TMDT_LT.Services.PromotionEngine>();

var app = builder.Build();

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.MapStaticAssets();

// 1. Khởi tạo định tuyến trước
app.UseRouting();

// 2. Gọi Session ngay sau Routing
app.UseSession();

app.UseAuthorization();

// 3. Map Controller cuối cùng
app.MapControllerRoute(
    name: "areas",
    pattern: "{area:exists}/{controller=Product}/{action=Index}/{id?}");

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");

app.Run();