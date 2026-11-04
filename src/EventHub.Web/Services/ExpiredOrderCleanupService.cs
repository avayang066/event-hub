using Microsoft.Extensions.Options;

namespace EventHub.Web.Services;

/// <summary>逾期訂單排程設定，對應 appsettings 的 "OrderExpiration" 區段。</summary>
public class OrderExpirationOptions
{
    public const string SectionName = "OrderExpiration";

    /// <summary>是否啟用排程（測試環境關閉，改由測試直接呼叫）</summary>
    public bool Enabled { get; set; } = true;

    /// <summary>檢查間隔（秒）</summary>
    public int IntervalSeconds { get; set; } = 60;
}

/// <summary>
/// 背景排程：定期取消超過付款期限的待付款訂單並釋放名額。
/// 隨網站啟動，網站關閉時停止。
/// </summary>
public class ExpiredOrderCleanupService(
    IServiceScopeFactory scopeFactory,
    IOptions<OrderExpirationOptions> options,
    ILogger<ExpiredOrderCleanupService> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
        {
            logger.LogInformation("逾期訂單排程未啟用");
            return;
        }

        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.IntervalSeconds)));

        do
        {
            try
            {
                // DbContext 是 Scoped，每次執行建立新的 scope
                using var scope = scopeFactory.CreateScope();
                var orderState = scope.ServiceProvider.GetRequiredService<OrderStateService>();
                await orderState.CancelExpiredAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception ex)
            {
                // 單次失敗（例如資料庫暫時連不上）不讓排程停掉，下一輪再試
                logger.LogError(ex, "取消逾期訂單時發生錯誤");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken));
    }
}
