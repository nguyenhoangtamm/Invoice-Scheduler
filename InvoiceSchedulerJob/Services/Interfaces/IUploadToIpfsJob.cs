namespace InvoiceSchedulerJob.Services.Interfaces;

public interface IUploadToIpfsJob
{
    Task ExecuteAsync(CancellationToken cancellationToken = default);
    Task ExecuteAsync(bool forceRun, bool dryRun = false, CancellationToken cancellationToken = default);
}