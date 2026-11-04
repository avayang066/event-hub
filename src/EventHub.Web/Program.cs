using EventHub.Web.Data;
using EventHub.Web.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

// ---- 資料庫（MySQL / Pomelo）----
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("缺少 ConnectionStrings:Default 設定");

builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, new MySqlServerVersion(new Version(8, 0, 30))));
builder.Services.AddDatabaseDeveloperPageExceptionFilter();

// ---- 會員與角色（ASP.NET Core Identity）----
builder.Services
    .AddDefaultIdentity<ApplicationUser>(options =>
    {
        options.SignIn.RequireConfirmedAccount = false;
        options.Password.RequireNonAlphanumeric = false;
        options.Password.RequiredLength = 8;
    })
    .AddRoles<IdentityRole>()
    .AddEntityFrameworkStores<AppDbContext>();

// ---- 設定綁定 ----
builder.Services.Configure<EcpayOptions>(builder.Configuration.GetSection(EcpayOptions.SectionName));
builder.Services.Configure<SmtpOptions>(builder.Configuration.GetSection(SmtpOptions.SectionName));

// ---- 反向代理（ngrok / 正式環境的 Nginx）：讓 Request.Scheme / RemoteIp 正確 ----
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services.AddControllersWithViews();
builder.Services.AddRazorPages();

var app = builder.Build();

app.UseForwardedHeaders();

if (app.Environment.IsDevelopment())
{
    app.UseMigrationsEndPoint();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();
app.UseStaticFiles();

app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllerRoute(
    name: "default",
    pattern: "{controller=Home}/{action=Index}/{id?}");
app.MapRazorPages();

if (!app.Environment.IsEnvironment("Testing"))
{
    await DbSeeder.SeedRolesAsync(app.Services);
}

app.Run();

// 讓整合測試（WebApplicationFactory）可以參照
public partial class Program;
