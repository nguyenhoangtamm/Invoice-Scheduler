using Hangfire;
using InvoiceSchedulerJob.Services.Interfaces;
using Microsoft.AspNetCore.Mvc;

namespace InvoiceSchedulerJob.Controllers;

[ApiController]
[Route("api/[controller]")]
public class JobsController : ControllerBase
{
    private readonly IBackgroundJobClient _backgroundJobClient;
    private readonly IRecurringJobManager _recurringJobManager;
    private readonly ILogger<JobsController> _logger;

    public JobsController(
        IBackgroundJobClient backgroundJobClient,
        IRecurringJobManager recurringJobManager,
        ILogger<JobsController> logger)
    {
        _backgroundJobClient = backgroundJobClient;
        _recurringJobManager = recurringJobManager;
        _logger = logger;
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
}