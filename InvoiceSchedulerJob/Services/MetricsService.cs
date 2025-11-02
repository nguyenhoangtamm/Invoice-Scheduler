using System.Diagnostics.Metrics;

namespace InvoiceSchedulerJob.Services;

public class MetricsService
{
    private readonly Meter _meter;
    private readonly Counter<int> _jobExecutionCounter;
    private readonly Counter<int> _jobSuccessCounter;
    private readonly Counter<int> _jobFailureCounter;
    private readonly Histogram<double> _jobDurationHistogram;
    private readonly Counter<int> _invoicesProcessedCounter;
    private readonly Counter<int> _batchesCreatedCounter;
    private readonly Counter<int> _blockchainSubmissionsCounter;
    private readonly Gauge<int> _pendingInvoicesGauge;
    private readonly Gauge<int> _pendingBatchesGauge;
    private readonly ILogger<MetricsService> _logger;

    public MetricsService(ILogger<MetricsService> logger)
    {
        _logger = logger;
        _meter = new Meter("InvoiceSchedulerJob", "1.0.0");

        // Job execution metrics
        _jobExecutionCounter = _meter.CreateCounter<int>(
            "job_executions_total",
            description: "Total number of job executions");

        _jobSuccessCounter = _meter.CreateCounter<int>(
            "job_successes_total",
            description: "Total number of successful job executions");

        _jobFailureCounter = _meter.CreateCounter<int>(
            "job_failures_total",
            description: "Total number of failed job executions");

        _jobDurationHistogram = _meter.CreateHistogram<double>(
            "job_duration_seconds",
            description: "Job execution duration in seconds");

        // Business metrics
        _invoicesProcessedCounter = _meter.CreateCounter<int>(
            "invoices_processed_total",
            description: "Total number of invoices processed");

        _batchesCreatedCounter = _meter.CreateCounter<int>(
            "batches_created_total",
            description: "Total number of batches created");

        _blockchainSubmissionsCounter = _meter.CreateCounter<int>(
            "blockchain_submissions_total",
            description: "Total number of blockchain submissions");

        _pendingInvoicesGauge = _meter.CreateGauge<int>(
            "pending_invoices",
            description: "Number of invoices pending processing");

        _pendingBatchesGauge = _meter.CreateGauge<int>(
            "pending_batches",
            description: "Number of batches pending blockchain submission");
    }

    public void RecordJobExecution(string jobType, bool success, double durationSeconds)
    {
        var tags = new KeyValuePair<string, object?>[] { new("job_type", jobType), new("status", success ? "success" : "failure") };

        _jobExecutionCounter.Add(1, tags);
        _jobDurationHistogram.Record(durationSeconds, tags);

        if (success)
        {
            _jobSuccessCounter.Add(1, new KeyValuePair<string, object?>[] { new("job_type", jobType) });
        }
        else
        {
            _jobFailureCounter.Add(1, new KeyValuePair<string, object?>[] { new("job_type", jobType) });
        }

        _logger.LogInformation(
            "Job execution recorded: {JobType} - {Status} - {Duration}s",
            jobType, success ? "SUCCESS" : "FAILURE", durationSeconds);
    }

    public void RecordInvoicesProcessed(int count, string operation)
    {
        _invoicesProcessedCounter.Add(count, new KeyValuePair<string, object?>[] { new("operation", operation) });
    }

    public void RecordBatchesCreated(int count)
    {
        _batchesCreatedCounter.Add(count);
    }

    public void RecordBlockchainSubmissions(int count, string result)
    {
        _blockchainSubmissionsCounter.Add(count, new KeyValuePair<string, object?>[] { new("result", result) });
    }

    public void UpdatePendingInvoices(int count)
    {
        _pendingInvoicesGauge.Record(count);
    }

    public void UpdatePendingBatches(int count)
    {
        _pendingBatchesGauge.Record(count);
    }
}