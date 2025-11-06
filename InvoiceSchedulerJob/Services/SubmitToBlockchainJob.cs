using InvoiceSchedulerJob.Configuration;
using InvoiceSchedulerJob.Data;
using InvoiceSchedulerJob.Entites;
using InvoiceSchedulerJob.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace InvoiceSchedulerJob.Services;

public class SubmitToBlockchainJob : ISubmitToBlockchainJob
{
    private readonly InvoiceDbContext _dbContext;
    private readonly IBlockchainService _blockchainService;
    private readonly JobConfiguration _jobConfig;
    private readonly BlockchainConfiguration _blockchainConfig;
    private readonly ILogger<SubmitToBlockchainJob> _logger;
    private readonly string _workerId;

    public SubmitToBlockchainJob(
        InvoiceDbContext dbContext,
        IBlockchainService blockchainService,
        IOptions<JobConfiguration> jobConfig,
        IOptions<BlockchainConfiguration> blockchainConfig,
        ILogger<SubmitToBlockchainJob> logger)
    {
        _dbContext = dbContext;
        _blockchainService = blockchainService;
        _jobConfig = jobConfig.Value;
        _blockchainConfig = blockchainConfig.Value;
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
            "Starting SubmitToBlockchainJob {JobId} (Force: {ForceRun}, DryRun: {DryRun}, Worker: {WorkerId})",
            jobId, forceRun, dryRun, _workerId);

        try
        {
            // First, check for pending transactions
            await CheckPendingTransactionsAsync(cancellationToken);

            // Then, submit new batches
            var readyBatches = await GetReadyBatchesAsync(cancellationToken);

            if (!readyBatches.Any())
            {
                _logger.LogInformation("No batches ready for blockchain submission");
                return;
            }

            _logger.LogInformation("Found {BatchCount} batches ready for blockchain submission", readyBatches.Count);

            var successCount = 0;
            var failureCount = 0;

            foreach (var batch in readyBatches)
            {
                var success = await ProcessBatchAsync(batch, dryRun, cancellationToken);
                if (success)
                {
                    successCount++;
                }
                else
                {
                    failureCount++;
                }

                // Add delay between submissions to avoid overwhelming the RPC
                if (!dryRun && readyBatches.Count > 1)
                {
                    await Task.Delay(2000, cancellationToken);
                }
            }

            var duration = DateTime.UtcNow - startTime;
            _logger.LogInformation(
                "SubmitToBlockchainJob {JobId} completed in {Duration}ms. Success: {SuccessCount}, Failed: {FailureCount}",
                jobId, duration.TotalMilliseconds, successCount, failureCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "SubmitToBlockchainJob {JobId} failed with error", jobId);
            throw;
        }
    }

    private async Task<List<InvoiceBatch>> GetReadyBatchesAsync(CancellationToken cancellationToken)
    {
        return await _dbContext.InvoiceBatches
            .Include(b => b.Invoices)
            .Where(b => b.Status == BatchStatus.Initial &&
                       !string.IsNullOrEmpty(b.MerkleRoot))
            .OrderBy(b => b.CreatedAt)
            .Take(10) // Limit to prevent overwhelming the blockchain
            .ToListAsync(cancellationToken);
    }

    private async Task<bool> ProcessBatchAsync(InvoiceBatch batch, bool dryRun, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("Processing batch {BatchId} for blockchain submission", batch.BatchId);

            if (dryRun)
            {
                _logger.LogInformation(
                    "DRY RUN: Would submit batch {BatchId} with merkle root {MerkleRoot} to blockchain",
                    batch.BatchId, batch.MerkleRoot);
                return true;
            }

            // Check if already submitted
            if (!string.IsNullOrEmpty(batch.TxHash))
            {
                _logger.LogDebug("Batch {BatchId} already has tx hash {TxHash}, checking status", batch.BatchId, batch.TxHash);
                return await CheckTransactionStatusAsync(batch, cancellationToken);
            }

            // Start transaction to mark as processing
            using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

            try
            {
                // Re-fetch and lock the batch
                var lockedBatch = await _dbContext.InvoiceBatches
                    .Where(b => b.Id == batch.Id &&
                               b.Status == BatchStatus.Initial &&
                               string.IsNullOrEmpty(b.TxHash))
                    .FirstOrDefaultAsync(cancellationToken);

                if (lockedBatch == null)
                {
                    // Batch was already processed by another worker
                    try
                    {
                        await transaction.RollbackAsync(cancellationToken);
                    }
                    catch (Exception rollbackEx)
                    {
                        _logger.LogWarning(rollbackEx, "Failed to rollback transaction for batch {BatchId}", batch.BatchId);
                    }
                    _logger.LogDebug("Batch {BatchId} already processed by another worker", batch.BatchId);
                    return true;
                }

                // Mark TxHash placeholder to prevent re-submission
                // We don't change status here since we still need to track it as Initial until confirmed
                lockedBatch.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                await transaction.CommitAsync(cancellationToken);

                // Calculate batch size (number of invoices in this batch)
                var batchSize = lockedBatch.Invoices?.Count ?? 0;
                if (batchSize == 0)
                {
                    // Fallback to database count if navigation property is null
                    batchSize = await _dbContext.Invoices
                        .Where(i => i.BatchId == lockedBatch.Id)
                        .CountAsync(cancellationToken);
                }

                // Submit to blockchain using anchorBatch function (Invoice4.sol)
                // The metadataURI should point to IPFS URI containing batch metadata
                var txHash = await _blockchainService.SubmitBatchAsync(
                    lockedBatch.MerkleRoot!,
                    batchSize,
                    lockedBatch.BatchCid, // This is the IPFS metadata URI
                    cancellationToken);

                // Update batch with transaction hash
                using var updateTransaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);

                var batchToUpdate = await _dbContext.InvoiceBatches.FindAsync(batch.Id, cancellationToken);
                if (batchToUpdate == null)
                {
                    throw new InvalidOperationException($"Batch {batch.Id} not found for update");
                }

                batchToUpdate.TxHash = txHash;
                batchToUpdate.UpdatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync(cancellationToken);
                await updateTransaction.CommitAsync(cancellationToken);

                _logger.LogInformation(
                    "Successfully submitted batch {BatchId} to blockchain with tx hash: {TxHash}",
                    batch.BatchId, txHash);

                return true;
            }
            catch (Exception)
            {
                try
                {
                    await transaction.RollbackAsync(cancellationToken);
                }
                catch (Exception rollbackEx)
                {
                    _logger.LogWarning(rollbackEx, "Failed to rollback transaction for batch {BatchId}", batch.BatchId);
                }
                throw;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to submit batch {BatchId} to blockchain", batch.BatchId);

            try
            {
                // Mark batch as failed
                var failedBatch = await _dbContext.InvoiceBatches.FindAsync(batch.Id, cancellationToken);
                if (failedBatch != null)
                {
                    failedBatch.Status = BatchStatus.BlockchainFailed;
                    failedBatch.UpdatedAt = DateTime.UtcNow;

                    // Reset associated invoices
                    var batchInvoices = await _dbContext.Invoices
                        .Where(i => i.BatchId == batch.Id)
                        .ToListAsync(cancellationToken);

                    foreach (var invoice in batchInvoices)
                    {
                        invoice.Status = InvoiceStatus.BlockchainFailed;
                        invoice.UpdatedAt = DateTime.UtcNow;
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);
                }
            }
            catch (Exception updateEx)
            {
                _logger.LogError(updateEx, "Failed to update batch {BatchId} status to failed", batch.BatchId);
            }

            return false;
        }
    }

    private async Task CheckPendingTransactionsAsync(CancellationToken cancellationToken)
    {
        _logger.LogDebug("Checking pending blockchain transactions");

        // Check batches in Initial status that already have TxHash (submitted but not yet confirmed)
        var pendingBatches = await _dbContext.InvoiceBatches
            .Where(b => b.Status == BatchStatus.Initial &&
                       !string.IsNullOrEmpty(b.TxHash))
            .ToListAsync(cancellationToken);

        foreach (var batch in pendingBatches)
        {
            await CheckTransactionStatusAsync(batch, cancellationToken);
        }
    }

    private async Task<bool> CheckTransactionStatusAsync(InvoiceBatch batch, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(batch.TxHash))
        {
            return false;
        }

        try
        {
            _logger.LogDebug("Checking transaction status for batch {BatchId}, tx {TxHash}", batch.BatchId, batch.TxHash);

            var isConfirmed = await _blockchainService.IsTransactionConfirmedAsync(
                batch.TxHash,
                _blockchainConfig.ConfirmationBlocks,
                cancellationToken);

            if (isConfirmed)
            {
                var receipt = await _blockchainService.GetTransactionReceiptAsync(batch.TxHash, cancellationToken);

                if (receipt != null && receipt.Status?.Value == 1)
                {
                    // Transaction successful
                    batch.Status = BatchStatus.BlockchainConfirmed;
                    batch.BlockNumber = (long?)receipt.BlockNumber?.Value;
                    batch.ConfirmedAt = DateTime.UtcNow;
                    batch.UpdatedAt = DateTime.UtcNow;

                    // Update associated invoices
                    var batchInvoices = await _dbContext.Invoices
                        .Where(i => i.BatchId == batch.Id)
                        .ToListAsync(cancellationToken);

                    foreach (var invoice in batchInvoices)
                    {
                        invoice.Status = InvoiceStatus.BlockchainConfirmed;
                        invoice.UpdatedAt = DateTime.UtcNow;
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);

                    _logger.LogInformation(
                        "Batch {BatchId} confirmed on blockchain at block {BlockNumber}",
                        batch.BatchId, batch.BlockNumber);

                    return true;
                }
                else
                {
                    // Transaction failed
                    batch.Status = BatchStatus.BlockchainFailed;
                    batch.UpdatedAt = DateTime.UtcNow;

                    var batchInvoices = await _dbContext.Invoices
                        .Where(i => i.BatchId == batch.Id)
                        .ToListAsync(cancellationToken);

                    foreach (var invoice in batchInvoices)
                    {
                        invoice.Status = InvoiceStatus.BlockchainFailed;
                        invoice.UpdatedAt = DateTime.UtcNow;
                    }

                    await _dbContext.SaveChangesAsync(cancellationToken);

                    _logger.LogWarning(
                        "Batch {BatchId} transaction {TxHash} failed on blockchain",
                        batch.BatchId, batch.TxHash);

                    return false;
                }
            }
            else
            {
                // Still pending, check if it's been too long
                var timeSinceUpdate = DateTime.UtcNow - (batch.UpdatedAt ?? batch.CreatedAt);
                if (timeSinceUpdate.TotalMinutes > _blockchainConfig.TimeoutMs / 60000.0)
                {
                    _logger.LogWarning(
                        "Batch {BatchId} transaction {TxHash} has been pending for {Minutes} minutes, marking as failed",
                        batch.BatchId, batch.TxHash, timeSinceUpdate.TotalMinutes);

                    batch.Status = BatchStatus.BlockchainFailed;
                    batch.UpdatedAt = DateTime.UtcNow;
                    await _dbContext.SaveChangesAsync(cancellationToken);
                    return false;
                }

                _logger.LogDebug(
                    "Batch {BatchId} transaction {TxHash} still pending confirmation",
                    batch.BatchId, batch.TxHash);
                return true;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to check transaction status for batch {BatchId}", batch.BatchId);
            return false;
        }
    }

    /// <summary>
    /// Register individual invoices on blockchain for better indexing
    /// This is optional and can be called after batch is anchored
    /// According to Invoice4.sol: registerIndividualInvoice function
    /// </summary>
    private async Task RegisterIndividualInvoicesAsync(InvoiceBatch batch, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(batch.MerkleRoot) || batch.Invoices == null || !batch.Invoices.Any())
        {
            _logger.LogDebug("Batch {BatchId} has no invoices to register", batch.BatchId);
            return;
        }

        _logger.LogInformation(
            "Registering {InvoiceCount} individual invoices for batch {BatchId}",
            batch.Invoices.Count, batch.BatchId);

        foreach (var invoice in batch.Invoices)
        {
            try
            {
                // Use CID or invoice hash based on what's available
                var invoiceCid = invoice.Cid ?? string.Empty;
                var invoiceHash = invoice.CidHash ?? "0x0"; // Default hash if not available

                if (string.IsNullOrEmpty(invoiceHash) || invoiceHash == "0x0")
                {
                    // Calculate hash from CID if not available
                    var cidToHash = !string.IsNullOrEmpty(invoiceCid)
                        ? invoiceCid
                        : invoice.InvoiceNumber ?? invoice.Id.ToString();

                    var hashBytes = System.Security.Cryptography.SHA256.HashData(
                        System.Text.Encoding.UTF8.GetBytes(cidToHash));
                    invoiceHash = "0x" + BitConverter.ToString(hashBytes).Replace("-", "");
                }

                await _blockchainService.RegisterIndividualInvoiceAsync(
                    batch.MerkleRoot,
                    invoice.InvoiceNumber ?? invoice.Id.ToString(),
                    invoiceCid,
                    invoiceHash,
                    cancellationToken);

                _logger.LogDebug(
                    "Successfully registered invoice {InvoiceId} for batch {BatchId}",
                    invoice.InvoiceNumber, batch.BatchId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "Failed to register invoice {InvoiceId} for batch {BatchId}, will continue with other invoices",
                    invoice.InvoiceNumber, batch.BatchId);
                // Don't fail the entire batch if individual invoice registration fails
            }
        }
    }
}