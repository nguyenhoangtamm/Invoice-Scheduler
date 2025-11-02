namespace InvoiceSchedulerJob.Services.Interfaces;

public interface IIpfsService
{
    Task<string> PinJsonAsync(object data, string fileName, CancellationToken cancellationToken = default);
    Task<string> PinJsonAsync(string json, string fileName, CancellationToken cancellationToken = default);
    Task<bool> IsPinnedAsync(string cid, CancellationToken cancellationToken = default);
    Task<string?> GetJsonAsync(string cid, CancellationToken cancellationToken = default);
}