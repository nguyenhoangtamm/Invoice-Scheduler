namespace InvoiceSchedulerJob.Services.Interfaces;

public interface ISubmitToBlockchainJob
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
    Task ExecuteAsync(bool forceRun, bool dryRun = false, CancellationToken cancellationToken = default);
}