using Hangfire;
using InvoiceSchedulerJob.Configuration;
using InvoiceSchedulerJob.Services.Interfaces;

namespace InvoiceSchedulerJob.Configuration;

public static class HangfireJobExtensions
{
    /// <summary>
    /// Configures recurring jobs for the application
    /// </summary>
    public static async Task ConfigureRecurringJobsAsync(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        try
        {
            var recurringJobManager = scope.ServiceProvider.GetRequiredService<IRecurringJobManager>();
            var jobConfig = scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Options.IOptions<JobConfiguration>>().Value;

            Console.WriteLine($"Configuring IPFS job with cron: {jobConfig.UploadCron}");

            // Upload to IPFS job
            recurringJobManager.AddOrUpdate<IUploadToIpfsJob>(
                "upload-to-ipfs",
                x => x.ExecuteAsync(CancellationToken.None),
                jobConfig.UploadCron,
                new RecurringJobOptions
                {
                    TimeZone = TimeZoneInfo.Local
                });

            Console.WriteLine("IPFS recurring job configured successfully");

            // Trigger job ngay l?p t?c ?? test (optional)
            // BackgroundJob.Enqueue<IUploadToIpfsJob>(x => x.ExecuteAsync(CancellationToken.None));

            //// Create batch job
            //recurringJobManager.AddOrUpdate<ICreateBatchJob>(
            //    "create-batch",
            //    "batch",
            //    x => x.ExecuteAsync(CancellationToken.None),
            //    jobConfig.BatchCron,
            //    new RecurringJobOptions
            //    {
            //        TimeZone = TimeZoneInfo.Local
            //    });

            //// Submit to blockchain job
            //recurringJobManager.AddOrUpdate<ISubmitToBlockchainJob>(
            //    "submit-to-blockchain",
            //    "blockchain",
            //    x => x.ExecuteAsync(CancellationToken.None),
            //    jobConfig.BlockchainCron,
            //    new RecurringJobOptions
            //    {
            //        TimeZone = TimeZoneInfo.Local
            //    });

            // Legacy jobs
            //recurringJobManager.AddOrUpdate<IBlockchainMonitor>(
            //    "monitor-blocks",
            //    "blockchain",
            //    x => x.MonitorLatestBlockAsync(),
            //    "*/1 * * * *", // Every minute
            //    new RecurringJobOptions
            //    {
            //        TimeZone = TimeZoneInfo.Local
            //    });

            //recurringJobManager.AddOrUpdate<IBlockchainMonitor>(
            //    "check-pending-transactions",
            //    "blockchain",
            //    x => x.CheckPendingTransactionsAsync(),
            //    "*/5 * * * *", // Every 5 minutes
            //    new RecurringJobOptions
            //    {
            //        TimeZone = TimeZoneInfo.Local
            //    });
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to configure recurring jobs: {ex.Message}");
            Console.WriteLine($"Stack trace: {ex.StackTrace}");
            throw;
        }
    }
}