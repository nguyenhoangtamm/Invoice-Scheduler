using Hangfire;
using InvoiceSchedulerJob.DTOs;
using InvoiceSchedulerJob.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using InvoiceSchedulerJob.Data;
using InvoiceSchedulerJob.Entites;

namespace InvoiceSchedulerJob.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly ILogger<JobsController> _logger;
    private readonly IBlockchainService _blockchainService;
    private readonly IIpfsService _ipfsService;
    private readonly InvoiceDbContext _dbContext;

    public JobsController(
        IBackgroundJobClient backgroundJobClient,
        IRecurringJobManager recurringJobManager,
        ILogger<JobsController> logger,
        IBlockchainService blockchainService,
        IIpfsService ipfsService,
        InvoiceDbContext dbContext)
    {
        _backgroundJobClient = backgroundJobClient;
        _recurringJobManager = recurringJobManager;
        _logger = logger;
        _blockchainService = blockchainService;
        _ipfsService = ipfsService;
        _dbContext = dbContext;
    }

    [HttpPost("upload-to-ipfs/trigger")]
    public IActionResult TriggerUploadToIpfs([FromQuery] bool forceRun = false, [FromQuery] bool dryRun = false)
    {
        var jobId = _backgroundJobClient.Enqueue<IUploadToIpfsJob>(
            x => x.ExecuteAsync(forceRun, dryRun, CancellationToken.None));

        _logger.LogInformation("Manually triggered UploadToIpfsJob with ID: {JobId}", jobId);

        return Ok(new { JobId = jobId, Message = "Upload to IPFS job triggered successfully" });
    }

    [HttpPost("create-batch/trigger")]
    public IActionResult TriggerCreateBatch([FromQuery] bool forceRun = false, [FromQuery] bool dryRun = false)
    {
        var jobId = _backgroundJobClient.Enqueue<ICreateBatchJob>(
            x => x.ExecuteAsync(forceRun, dryRun, CancellationToken.None));

        _logger.LogInformation("Manually triggered CreateBatchJob with ID: {JobId}", jobId);

        return Ok(new { JobId = jobId, Message = "Create batch job triggered successfully" });
    }

    [HttpPost("submit-to-blockchain/trigger")]
    public IActionResult TriggerSubmitToBlockchain([FromQuery] bool forceRun = false, [FromQuery] bool dryRun = false)
    {
        var jobId = _backgroundJobClient.Enqueue<ISubmitToBlockchainJob>(
            x => x.ExecuteAsync(forceRun, dryRun, CancellationToken.None));

        _logger.LogInformation("Manually triggered SubmitToBlockchainJob with ID: {JobId}", jobId);

        return Ok(new { JobId = jobId, Message = "Submit to blockchain job triggered successfully" });
    }

    [HttpPost("recurring/enable/{jobId}")]
    public IActionResult EnableRecurringJob(string jobId)
    {
        try
        {
            _recurringJobManager.Trigger(jobId);
            _logger.LogInformation("Enabled recurring job: {JobId}", jobId);
            return Ok(new { Message = $"Recurring job {jobId} enabled successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to enable recurring job: {JobId}", jobId);
            return BadRequest(new { Error = $"Failed to enable job: {ex.Message}" });
        }
    }

    [HttpDelete("recurring/disable/{jobId}")]
    public IActionResult DisableRecurringJob(string jobId)
    {
        try
        {
            _recurringJobManager.RemoveIfExists(jobId);
            _logger.LogInformation("Disabled recurring job: {JobId}", jobId);
            return Ok(new { Message = $"Recurring job {jobId} disabled successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to disable recurring job: {JobId}", jobId);
            return BadRequest(new { Error = $"Failed to disable job: {ex.Message}" });
        }
    }

    [HttpGet("status")]
    public IActionResult GetJobStatus()
    {
        // This would typically query the database for current status
        return Ok(new
        {
            Message = "Job status endpoint - implement database queries for actual status",
            Timestamp = DateTime.UtcNow,
            RecurringJobs = new[]
            {
                "upload-to-ipfs",
                "create-batch",
                "submit-to-blockchain",
                "monitor-blocks",
                "check-pending-transactions"
            }
        });
    }

    [HttpGet("verify-invoice/{invoiceId}")]
    public async Task<IActionResult> VerifyInvoice(int invoiceId)
    {
        try
        {
            // Fetch invoice with batch information
            var invoice = await _dbContext.Invoices
                .Include(i => i.Batch)
                .FirstOrDefaultAsync(i => i.Id == invoiceId);

            if (invoice == null)
            {
                return NotFound(new { Error = $"Invoice with ID {invoiceId} not found" });
            }

            if (string.IsNullOrEmpty(invoice.Cid))
            {
                return BadRequest(new { Error = "Invoice has no CID - not uploaded to IPFS" });
            }

            if (invoice.Batch == null || string.IsNullOrEmpty(invoice.Batch.MerkleRoot))
            {
                return BadRequest(new { Error = "Invoice is not part of a valid batch" });
            }

            if (string.IsNullOrEmpty(invoice.MerkleProof))
            {
                return BadRequest(new { Error = "Invoice has no Merkle proof" });
            }

            // Parse Merkle proof from JSON string
            string[]? merkleProofStrings;
            try
            {
                merkleProofStrings = JsonSerializer.Deserialize<string[]>(invoice.MerkleProof);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to parse Merkle proof for invoice {InvoiceId}", invoiceId);
                return BadRequest(new { Error = "Invalid Merkle proof format" });
            }

            if (merkleProofStrings == null || merkleProofStrings.Length == 0)
            {
                return BadRequest(new { Error = "Empty Merkle proof" });
            }

            // Convert string proofs to byte arrays
            var merkleProof = merkleProofStrings.Select(p => Convert.FromHexString(p.StartsWith("0x") ? p[2..] : p)).ToArray();

            var isValid = await _blockchainService.VerifyInvoiceAsync(invoice.Batch.MerkleRoot, invoice.Cid, merkleProof, CancellationToken.None);

            var response = new VerifyInvoiceResponseDto
            {
                IsValid = isValid,
                Message = isValid ? "Invoice verified successfully" : "Invoice verification failed"
            };

            if (isValid)
            {
                // Get batch information from blockchain
                var batchInfo = await _blockchainService.GetBatchAsync(invoice.Batch.MerkleRoot, CancellationToken.None);
                response.BatchInfo = batchInfo;

                // If batch has metadata URI, try to fetch metadata from IPFS
                if (batchInfo != null && !string.IsNullOrEmpty(batchInfo.MetadataUri))
                {
                    try
                    {
                        // Extract CID from IPFS URI (format: ipfs://<cid> or https://gateway/ipfs/<cid>)
                        var cid = ExtractCidFromUri(batchInfo.MetadataUri);
                        if (!string.IsNullOrEmpty(cid))
                        {
                            var metadataJson = await _ipfsService.GetJsonAsync(cid, CancellationToken.None);
                            response.MetadataJson = metadataJson;
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to fetch metadata from IPFS for batch {MerkleRoot}", invoice.Batch.MerkleRoot);
                        // Don't fail the whole request if IPFS fetch fails
                    }
                }

                _logger.LogInformation("Invoice verified successfully: InvoiceId={InvoiceId}, MerkleRoot={MerkleRoot}, InvoiceCid={InvoiceCid}", invoiceId, invoice.Batch.MerkleRoot, invoice.Cid);
            }
            else
            {
                _logger.LogWarning("Invoice verification failed: InvoiceId={InvoiceId}, MerkleRoot={MerkleRoot}, InvoiceCid={InvoiceCid}", invoiceId, invoice.Batch.MerkleRoot, invoice.Cid);
            }

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error verifying invoice: InvoiceId={InvoiceId}", invoiceId);
            return StatusCode(500, new { Error = $"Internal server error: {ex.Message}" });
        }
    }

    private string? ExtractCidFromUri(string uri)
    {
        if (string.IsNullOrEmpty(uri))
            return null;

        // Handle ipfs://<cid> format
        if (uri.StartsWith("ipfs://"))
        {
            return uri[7..]; // Remove "ipfs://" prefix
        }

        // Handle gateway URLs like https://gateway.pinata.cloud/ipfs/<cid>
        var ipfsIndex = uri.IndexOf("/ipfs/");
        if (ipfsIndex >= 0)
        {
            return uri[(ipfsIndex + 6)..]; // Extract CID after "/ipfs/"
        }

        return null;
    }
}