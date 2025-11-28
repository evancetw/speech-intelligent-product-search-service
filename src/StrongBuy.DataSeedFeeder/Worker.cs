namespace StrongBuy.DataSeedFeeder;

public class Worker(
    IHostApplicationLifetime applicationLifetime,
    ILogger<Worker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (logger.IsEnabled(LogLevel.Information))
        {
            logger.LogInformation("Worker running at: {time}", DateTimeOffset.Now);
        }

        // await Task.Delay(1000, stoppingToken);


        applicationLifetime.StopApplication();
    }

    private async Task CreateFakeProductsAsync(CancellationToken stoppingToken)
    {
        
    }
}