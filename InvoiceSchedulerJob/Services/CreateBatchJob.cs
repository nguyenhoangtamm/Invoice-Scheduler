using System.Text.Json;
using InvoiceSchedulerJob.Configuration;
using InvoiceSchedulerJob.Data;
using InvoiceSchedulerJob.Models;
using InvoiceSchedulerJob.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InvoiceSchedulerJob.Services;

public class CreateBatchJob : ICreateBatchJob
{
    private readonly InvoiceDbContext _dbContext;
    private readonly IIpfsService _ipfsService;
    private readonly MerkleTreeService _merkleTreeService;
    private readonly JobConfiguration _jobConfig;
    private readonly ILogger<CreateBatchJob> _logger;
    private readonly string _workerId;

    public CreateBatchJob(
        InvoiceDbContext dbContext,
        IIpfsService ipfsService,
        MerkleTreeService merkleTreeService,
        IOptions<JobConfiguration> jobConfig,
        ILogger<CreateBatchJob> logger)
    {
        _dbContext = dbContext;
        _ipfsService = ipfsService;
        _merkleTreeService = merkleTreeService;
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
            "Starting CreateBatchJob {JobId} (Force: {ForceRun}, DryRun: {DryRun}, Worker: {WorkerId})",
            jobId, forceRun, dryRun, _workerId);

        try
        {
            var invoices = await GetReadyInvoicesAsync(cancellationToken);

            if (invoices.Count < (_jobConfig.BatchSize / 2) && !forceRun)
            {
                _logger.LogInformation(
                    "Found only {InvoiceCount} invoices ready for batching, waiting for more (minimum: {MinBatch})",
                    invoices.Count, _jobConfig.BatchSize / 2);
                return;
            }

            if (!invoices.Any())
            {
                _logger.LogInformation("No invoices ready for batching");
                return;
            }

            // Group invoices into batches
            var batches = CreateBatches(invoices);
            var processedBatches = 0;

            foreach (var batchInvoices in batches)
            {
                var success = await ProcessBatchAsync(batchInvoices, dryRun, cancellationToken);
                if (success)
                {
                    processedBatches++;
                }
            }

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "CreateBatchJob {JobId} completed in {Duration}ms. Processed {ProcessedBatches}/{TotalBatches} batches",
                jobId, duration.TotalMilliseconds, processedBatches, batches.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "CreateBatchJob {JobId} failed with error", jobId);
            throw;
        }
    }

    private async Task<List<Invoice>> GetReadyInvoicesAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.Invoices
            .Where(i => i.Status == InvoiceStatus.IpfsStored &&
                       !string.IsNullOrEmpty(i.Cid) &&
                       i.BatchId == null)
            .OrderBy(i => i.CreatedAt)
            .Take(_jobConfig.BatchSize * _jobConfig.BatchesPerRun) 
            .ToListAsync(cancellationToken);
    }

    private List<List<Invoice>> CreateBatches(List<Invoice> invoices)
    {
        var batches = new List<List<Invoice>>();

        for (int i = 0; i < invoices.Count; i += _jobConfig.BatchSize)
        {
            var batch = invoices.Skip(i).Take(_jobConfig.BatchSize).ToList();
            batches.Add(batch);
        }

        return batches;
    }

    private async Task<bool> ProcessBatchAsync(List<Invoice> invoices, bool dryRun, CancellationToken cancellationToken)
    {
        var batchId = GenerateBatchId();

        try
        {
            _logger.LogDebug("Processing batch {BatchId} with {InvoiceCount} invoices", batchId, invoices.Count);

            if (dryRun)
            {
                _logger.LogInformation(
                    "DRY RUN: Would create batch {BatchId} with {InvoiceCount} invoices",
                    batchId, invoices.Count);
                return true;
            }

            // Start transaction to claim invoices and create batch
            using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Create batch record
                var batch = new InvoiceBatch
                {
                    BatchId = batchId,
                    Count = invoices.Count,
                    Status = BatchStatus.Processing,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };

                _dbContext.InvoiceBatches.Add(batch);
                await _dbContext.SaveChangesAsync(cancellationToken);

                // Claim invoices for this batch
                var claimedInvoices = new List<Invoice>();
                foreach (var invoice in invoices)
                {
                    var lockedInvoice = await _dbContext.Invoices
                        .Where(i => i.Id == invoice.Id &&
                                   i.Status == InvoiceStatus.IpfsStored &&
                                   i.BatchId == null)
                        .FirstOrDefaultAsync(cancellationToken);

                    if (lockedInvoice != null)
                    {
                        lockedInvoice.BatchId = batch.Id;
                        lockedInvoice.Status = InvoiceStatus.Batched;
                        lockedInvoice.UpdatedAt = DateTime.UtcNow;
                        claimedInvoices.Add(lockedInvoice);
                    }
                }

                if (!claimedInvoices.Any())
                {
                    // No invoices were available, rollback
                    await transaction.RollbackAsync(cancellationToken);
                    _logger.LogDebug("No invoices available for batch {BatchId}, skipping", batchId);
                    return true;
                }

                // Update batch count with actual claimed invoices
                batch.Count = claimedInvoices.Count;
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                // Build Merkle tree (outside transaction)
                var cids = claimedInvoices
                    .Where(i => !string.IsNullOrEmpty(i.Cid))
                    .Select(i => i.Cid!)
                    .ToList();

                if (!cids.Any())
                {
                    throw new InvalidOperationException($"No valid CIDs found for batch {batchId}");
                }

                // Create batch CIDs JSON and upload to IPFS
                var batchCids = new
                {
                    Cids = cids
                };
                var batchFileName = $"batch-cids-{batchId}-{DateTime.UtcNow:yyyyMMdd-HHmmss}.json";
                var batchCid = await _ipfsService.PinJsonAsync(batchCids, batchFileName, cancellationToken);

                var merkleResult = _merkleTreeService.BuildTree(cids);

                // Update batch and invoices with Merkle data
                using var updateTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                var batchToUpdate = await _dbContext.InvoiceBatches.FindAsync(batch.Id, cancellationToken);
                if (batchToUpdate == null)
                {
                    throw new InvalidOperationException($"Batch {batch.Id} not found for update");
                }

                batchToUpdate.MerkleRoot = merkleResult.Root;
                batchToUpdate.BatchCid = batchCid;
                batchToUpdate.Status = BatchStatus.ReadyToSend;
                batchToUpdate.UpdatedAt = DateTime.UtcNow;

                // Update invoices with Merkle proofs and set status to ready for blockchain
                foreach (var invoice in claimedInvoices)
                {
                    if (!string.IsNullOrEmpty(invoice.Cid) && merkleResult.Proofs.ContainsKey(invoice.Cid))
                    {
                        var invoiceToUpdate = await _dbContext.Invoices.FindAsync(invoice.Id, cancellationToken);
                        if (invoiceToUpdate != null)
                        {
                            invoiceToUpdate.MerkleProof = JsonSerializer.Serialize(merkleResult.Proofs[invoice.Cid]);
                            invoiceToUpdate.Status = InvoiceStatus.BlockchainPending;
                            invoiceToUpdate.UpdatedAt = DateTime.UtcNow;
                        }
                    }
                }

                await _dbContext.SaveChangesAsync(cancellationToken);
                await updateTransaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Successfully created batch {BatchId} with {InvoiceCount} invoices. Merkle root: {MerkleRoot}, Batch CID: {BatchCid}",
                    batchId, claimedInvoices.Count, merkleResult.Root, batchCid);

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
            _logger.LogError(ex, "Failed to create batch {BatchId}", batchId);

            try
            {
                // Mark any created batch as failed
                var failedBatch = await _dbContext.InvoiceBatches
                    .Where(b => b.BatchId == batchId)
                    .FirstOrDefaultAsync(cancellationToken);

                if (failedBatch != null)
                {
                    failedBatch.Status = BatchStatus.BlockchainFailed;
                    failedBatch.UpdatedAt = DateTime.UtcNow;

                    // Reset invoice status for retry
                    var batchInvoices = await _dbContext.Invoices
                        .Where(i => i.BatchId == failedBatch.Id)
                        .ToListAsync(cancellationToken);

                    foreach (var invoice in batchInvoices)
                    {
                        invoice.BatchId = null;
                        invoice.Status = InvoiceStatus.IpfsStored;
                        invoice.MerkleProof = null;
                        invoice.UpdatedAt = DateTime.UtcNow;
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx, "Failed to update batch {BatchId} status to failed", batchId);
            }

            return false;
        }
    }

    private string GenerateBatchId()
    {
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        var random = Random.Shared.Next(1000, 9999);
        return $"BATCH-{timestamp}-{random}";
    }
}