using EventHub.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Tests;

/// <summary>
/// 整合測試用的網站：
/// - 資料庫換成暫存資料夾裡的 SQLite 檔案（支援條件式 UPDATE、交易、外鍵、check constraint，行為接近 MySQL），
///   不會碰到 Laragon 的 MySQL。每個請求各自開連線，可以測試多人同時報名。
/// - 上傳檔案也存到同一個暫存資料夾，測試結束後整個刪除
/// </summary>
public class TestWebAppFactory : WebApplicationFactory<Program>
{
    private readonly string _tempRoot =
        Path.Combine(Path.GetTempPath(), "eventhub-tests", Guid.NewGuid().ToString("N"));

    private readonly string _connectionString;

    public string UploadRoot => Path.Combine(_tempRoot, "uploads");

    public TestWebAppFactory()
    {
        Directory.CreateDirectory(_tempRoot);
        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = Path.Combine(_tempRoot, "test.db"),
            // 多人同時寫入時，等待鎖釋放的上限
            DefaultTimeout = 30,
        }.ToString();

        // 網站啟動時會建立角色與管理員帳號，所以要先把資料表建好
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connectionString).Options);
        db.Database.EnsureCreated();
    }

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");
        builder.UseSetting("ConnectionStrings:Default", "Server=unused");
        builder.UseSetting("Uploads:RootPath", UploadRoot);

        builder.ConfigureServices(services =>
        {
            services.RemoveAll<DbContextOptions<AppDbContext>>();
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connectionString));
        });
    }

    /// <summary>直接操作資料庫（準備測試資料、檢查結果）。</summary>
    public async Task WithDbAsync(Func<AppDbContext, Task> action)
    {
        using var scope = Services.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    public async Task<T> WithDbAsync<T>(Func<AppDbContext, Task<T>> action)
    {
        using var scope = Services.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);

        if (disposing)
        {
            // 關掉連線池，資料庫檔案才不會被鎖住而刪不掉
            SqliteConnection.ClearAllPools();
            try
            {
                if (Directory.Exists(_tempRoot))
                {
                    Directory.Delete(_tempRoot, recursive: true);
                }
            }
            catch (IOException)
            {
                // 暫存資料夾刪不掉不影響測試結果
            }
        }
    }
}

internal static class ServiceCollectionExtensions
{
    public static void RemoveAll<T>(this IServiceCollection services)
    {
        foreach (var d in services.Where(d => d.ServiceType == typeof(T)).ToList())
        {
            services.Remove(d);
        }
    }
}
