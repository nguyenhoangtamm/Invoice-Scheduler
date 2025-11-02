using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InvoiceSchedulerJob.Configuration;
using InvoiceSchedulerJob.Data;
using InvoiceSchedulerJob.Models;
using InvoiceSchedulerJob.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace InvoiceSchedulerJob.Services;

public class UploadToIpfsJob : IUploadToIpfsJob
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IIpfsService _ipfsService;
    private readonly JobConfiguration _jobConfig;
    private readonly ILogger<UploadToIpfsJob> _logger;
    private readonly string _workerId;

    public UploadToIpfsJob(
        IServiceProvider serviceProvider,
        IIpfsService ipfsService,
        IOptions<JobConfiguration> jobConfig,
        ILogger<UploadToIpfsJob> logger)
    {
        _serviceProvider = serviceProvider;
        _ipfsService = ipfsService;
        _jobConfig = jobConfig.Value;
        _logger = logger;
        _workerId = _jobConfig.WorkerId;
    }

    public async Task ExecuteAsync(CancellationToken cancellationToken = default)
    {
        await ExecuteAsync(false, _jobConfig.DryRunMode, cancellationToken);
    }

    public async Task ExecuteAsync(bool forceRun, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        var jobId = Guid.NewGuid().ToString("N")[..8];

        _logger.LogInformation(
            "Starting UploadToIpfsJob {JobId} (Force: {ForceRun}, DryRun: {DryRun}, Worker: {WorkerId})",
            jobId, forceRun, dryRun, _workerId);

        try
        {
            var invoices = await GetPendingInvoicesAsync(forceRun, cancellationToken);

            if (!invoices.Any())
            {
                _logger.LogInformation("No invoices pending IPFS upload");
                return;
            }

            _logger.LogInformation("Found {InvoiceCount} invoices to upload to IPFS", invoices.Count);

            var successCount = 0;
            var failureCount = 0;
            var semaphore = new SemaphoreSlim(_jobConfig.ConcurrentUploads, _jobConfig.ConcurrentUploads);

            var tasks = invoices.Select(async invoice =>
            {
                await semaphore.WaitAsync(cancellationToken);
                try
                {
                    var success = await ProcessInvoiceAsync(invoice, dryRun, cancellationToken);
                    if (success)
                    {
                        Interlocked.Increment(ref successCount);
                    }
                    else
                    {
                        Interlocked.Increment(ref failureCount);
                    }
                }
                finally
                {
                    semaphore.Release();
                }
            });

            await Task.WhenAll(tasks);

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "UploadToIpfsJob {JobId} completed in {Duration}ms. Success: {SuccessCount}, Failed: {FailureCount}",
                jobId, duration.TotalMilliseconds, successCount, failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "UploadToIpfsJob {JobId} failed with error", jobId);
            throw;
        }
    }

    private async Task<List<Invoice>> GetPendingInvoicesAsync(bool forceRun, CancellationToken cancellationToken)
    {
        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();

        var cutoffTime = forceRun ? DateTime.UtcNow : DateTime.UtcNow.AddMinutes(-1);

        return await dbContext.Invoices
            .Where(i => i.Status == InvoiceStatus.Uploaded &&
                       (string.IsNullOrEmpty(i.Cid) || i.Cid == "") &&
                       i.CreatedAt < cutoffTime)
            .Include(i => i.Lines)
            .OrderBy(i => i.CreatedAt)
            .Take(_jobConfig.MaxInvoicesPerRun) // Use config instead of hard-coded value
            .ToListAsync(cancellationToken);
    }

    private async Task<bool> ProcessInvoiceAsync(Invoice invoice, bool dryRun, CancellationToken cancellationToken)
    {
        var invoiceId = invoice.Id;

        using var scope = _serviceProvider.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InvoiceDbContext>();

        try
        {
            _logger.LogDebug("Processing invoice {InvoiceId} for IPFS upload", invoiceId);

            // Create canonical invoice data for IPFS
            var invoiceData = CreateCanonicalInvoiceData(invoice);
            var invoiceJson = JsonSerializer.Serialize(invoiceData, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
                WriteIndented = false
            });

            // Compute immutable hash
            var immutableHash = ComputeHash(invoiceJson);

            if (dryRun)
            {
                _logger.LogInformation(
                    "DRY RUN: Would upload invoice {InvoiceId} to IPFS (Hash: {Hash})",
                    invoiceId, immutableHash);
                return true;
            }

            // Start database transaction to claim the invoice
            using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Re-fetch and lock the invoice
                var lockedInvoice = await dbContext.Invoices
                    .Where(i => i.Id == invoiceId &&
                               i.Status == InvoiceStatus.Uploaded &&
                               (string.IsNullOrEmpty(i.Cid) || i.Cid == ""))
                    .FirstOrDefaultAsync(cancellationToken);

                if (lockedInvoice == null)
                {
                    // Invoice was already processed by another worker
                    await transaction.RollbackAsync(cancellationToken);
                    _logger.LogDebug("Invoice {InvoiceId} already processed by another worker", invoiceId);
                    return true;
                }

                // Mark as processing
                lockedInvoice.Status = InvoiceStatus.IpfsStored; // Temporarily mark as processing
                lockedInvoice.UpdatedAt = DateTime.UtcNow;
                await dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                // Upload to IPFS (outside transaction to avoid long-running transaction)
                var fileName = $"invoice-{invoiceId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
                var cid = await _ipfsService.PinJsonAsync(invoiceJson, fileName, cancellationToken);
                var cidHash = ComputeHash(cid);

                // Update invoice with IPFS data
                using var updateTransaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

                var invoiceToUpdate = await dbContext.Invoices
                    .FindAsync(new object[] { invoiceId }, cancellationToken);

                if (invoiceToUpdate == null)
                {
                    throw new InvalidOperationException($"Invoice {invoiceId} not found for update");
                }

                invoiceToUpdate.Cid = cid;
                invoiceToUpdate.CidHash = cidHash;
                invoiceToUpdate.ImmutableHash = immutableHash;
                invoiceToUpdate.Status = InvoiceStatus.IpfsStored;
                invoiceToUpdate.UpdatedAt = DateTime.UtcNow;

                await dbContext.SaveChangesAsync(cancellationToken);
                await updateTransaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Successfully uploaded invoice {InvoiceId} to IPFS: {CID}",
                    invoiceId, cid);

                return true;
            }
            catch (Exception)
            {
                await transaction.RollbackAsync(cancellationToken);
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to upload invoice {InvoiceId} to IPFS", invoiceId);

            try
            {
                // Mark invoice as failed
                var failedInvoice = await dbContext.Invoices.FindAsync(invoiceId, cancellationToken);
                if (failedInvoice != null)
                {
                    failedInvoice.Status = InvoiceStatus.IpfsFailed;
                    failedInvoice.UpdatedAt = DateTime.UtcNow;
                    await dbContext.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx, "Failed to update invoice {InvoiceId} status to failed", invoiceId);
            }

            return false;
        }
    }

    private object CreateCanonicalInvoiceData(Invoice invoice)
    {
        return new
        {
            invoice.Id,
            invoice.InvoiceNumber,
            invoice.FormNumber,
            invoice.Serial,
            invoice.TenantOrganizationId,
            invoice.IssuedByUserId,
            SellerInfo = new
            {
                invoice.SellerName,
                invoice.SellerTaxId,
                invoice.SellerAddress,
                invoice.SellerPhone,
                invoice.SellerEmail
            },
            CustomerInfo = new
            {
                invoice.CustomerName,
                invoice.CustomerTaxId,
                invoice.CustomerAddress,
                invoice.CustomerPhone,
                invoice.CustomerEmail
            },
            InvoiceDetails = new
            {
                invoice.IssueDate,
                invoice.SubTotal,
                invoice.TaxAmount,
                invoice.DiscountAmount,
                invoice.TotalAmount,
                invoice.Currency,
                invoice.Note
            },
            Lines = invoice.Lines.Select(line => new
            {
                line.LineNumber,
                line.Description,
                line.Unit,
                line.Quantity,
                line.UnitPrice,
                line.Discount,
                line.TaxRate,
                line.TaxAmount,
                line.LineTotal
            }).ToList(),
            Metadata = new
            {
                CreatedAt = invoice.CreatedAt.ToString("yyyy-MM-ddTHH:mm:ss.fffZ"),
                Version = "1.0"
            }
        };
    }

    private static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}