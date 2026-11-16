using Microsoft.Extensions.Options;

namespace EventHub.Web.Services.Email;

/// <summary>報名成功信設定，對應 appsettings 的 "ConfirmationEmail" 區段。</summary>
public class ConfirmationEmailOptions
{
    public const string SectionName = "ConfirmationEmail";

    /// <summary>補寄（重寄失敗的信）的檢查間隔（秒）</summary>
    public int RetryIntervalSeconds { get; set; } = 60;
}

/// <summary>
/// 背景寄信：
/// 1. 佇列裡有訂單編號就馬上寄
/// 2. 每隔一段時間補寄失敗或漏掉的信
/// </summary>
public class ConfirmationEmailBackgroundService(
    ConfirmationEmailQueue queue,
    IServiceScopeFactory scopeFactory,
    IOptions<ConfirmationEmailOptions> options,
    ILogger<ConfirmationEmailBackgroundService> logger) : BackgroundService
{
    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(ProcessQueueAsync(stoppingToken), RetryPendingAsync(stoppingToken));

    private async Task ProcessQueueAsync(CancellationToken stoppingToken)
    {
        try
        {
            await foreach (var orderNo in queue.ReadAllAsync(stoppingToken))
            {
                try
                {
                    // DbContext 是 Scoped，每封信建立新的 scope
                    using var scope = scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<ConfirmationEmailService>();
                    await service.SendAsync(orderNo, stoppingToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    // 單封失敗不影響後面的信；沒寄出的由補寄處理
                    logger.LogError(ex, "處理報名成功信時發生錯誤 訂單 {OrderNo}", orderNo);
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 網站關閉
        }
    }

    private async Task RetryPendingAsync(CancellationToken stoppingToken)
    {
        // 第一次在一個間隔之後才執行（網站剛啟動時讓佇列先處理）
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(Math.Max(5, options.Value.RetryIntervalSeconds)));

        try
        {
            while (await timer.WaitForNextTickAsync(stoppingToken))
            {
                try
                {
                    using var scope = scopeFactory.CreateScope();
                    var service = scope.ServiceProvider.GetRequiredService<ConfirmationEmailService>();
                    var sent = await service.SendPendingAsync(stoppingToken);
                    if (sent > 0)
                    {
                        logger.LogInformation("補寄報名成功信 {Count} 封", sent);
                    }
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    logger.LogError(ex, "補寄報名成功信時發生錯誤");
                }
            }
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // 網站關閉
        }
    }
}
