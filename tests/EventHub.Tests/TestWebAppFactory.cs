using EventHub.Web.Data;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EventHub.Tests;

/// <summary>
/// 整合測試用的網站：
/// - 資料庫換成 SQLite 記憶體資料庫（支援條件式 UPDATE、外鍵、check constraint，行為接近 MySQL），
///   不會碰到 Laragon 的 MySQL
/// - 上傳檔案存到暫存資料夾，測試結束後刪除
/// </summary>
public class TestWebAppFactory : WebApplicationFactory<Program>
{
    // SQLite 記憶體資料庫只在連線開著時存在，所以整個測試期間保持同一條連線
    private readonly SqliteConnection _connection = new("DataSource=:memory:");

    public string UploadRoot { get; } =
        Path.Combine(Path.GetTempPath(), "eventhub-tests", Guid.NewGuid().ToString("N"));

    public TestWebAppFactory()
    {
        _connection.Open();

        // 網站啟動時會建立角色與管理員帳號，所以要先把資料表建好
        using var db = new AppDbContext(
            new DbContextOptionsBuilder<AppDbContext>().UseSqlite(_connection).Options);
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
            services.AddDbContext<AppDbContext>(o => o.UseSqlite(_connection));
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
            _connection.Dispose();
            try
            {
                if (Directory.Exists(UploadRoot))
                {
                    Directory.Delete(UploadRoot, recursive: true);
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
