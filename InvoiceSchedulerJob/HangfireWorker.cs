public class HangfireWorker : BackgroundService
{
    private readonly ILogger<HangfireWorker> _logger;

    public HangfireWorker(ILogger<HangfireWorker> logger)
    {
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Hangfire Worker Service đang chạy");

        while (!stoppingToken.IsCancellationRequested)
        {
            _logger.LogInformation("Worker đang hoạt động lúc: {time}", DateTimeOffset.Now);
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    public override Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Hangfire Worker Service đang dừng");
        return base.StopAsync(cancellationToken);
    }
}