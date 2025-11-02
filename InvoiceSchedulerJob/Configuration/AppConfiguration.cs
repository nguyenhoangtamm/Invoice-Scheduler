namespace InvoiceSchedulerJob.Configuration;

public class IpfsConfiguration
{
    public const string SectionName = "Ipfs";

    /// <summary>
    /// Pinata JWT token cho API v3
    /// </summary>
    public string ApiKey { get; set; } = "238e275c8f91bbb38336";

    /// <summary>
    /// Deprecated: Sử dụng cho API v1 nếu cần
    /// </summary>
    public string ApiSecret { get; set; } = "6ea16908a81ef49e944cc0b1cb3ae157b249acfe516a9d72c72ba0ee30047787";

    /// <summary>
    /// Base URL cho Pinata API
    /// </summary>
    public string BaseUrl { get; set; } = "https://api.pinata.cloud";

    /// <summary>
    /// Endpoint để pin JSON
    /// </summary>
    public string PinEndpoint { get; set; } = "/pinning/pinJSONToIPFS";

    /// <summary>
    /// Số lần thử lại khi có lỗi
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Delay giữa các lần thử (milliseconds)
    /// </summary>
    public int RetryDelayMs { get; set; } = 1000;

    /// <summary>
    /// Timeout cho HTTP request (milliseconds)
    /// </summary>
    public int TimeoutMs { get; set; } = 30000;

    /// <summary>
    /// Rate limit số request mỗi phút
    /// </summary>
    public int RateLimitPerMinute { get; set; } = 20;

    /// <summary>
    /// Pinata Gateway URL
    /// </summary>
    public string GatewayUrl { get; set; } = "https://gateway.pinata.cloud";
}

public class BlockchainConfiguration
{
    public const string SectionName = "Blockchain";

    public string RpcUrl { get; set; } = string.Empty;
    public string ChainId { get; set; } = "11155111"; // Sepolia
    public string PrivateKey { get; set; } = string.Empty;
    public string? KmsEndpoint { get; set; }
    public string ContractAddress { get; set; } = string.Empty;
    public int MaxRetries { get; set; } = 3;
    public int RetryDelayMs { get; set; } = 2000;
    public int ConfirmationBlocks { get; set; } = 3;
    public long GasLimit { get; set; } = 200000;
    public long MaxGasPrice { get; set; } = 50000000000; // 50 Gwei
    public int TimeoutMs { get; set; } = 120000;
}

public class JobConfiguration
{
    public const string SectionName = "Jobs";

    public int BatchSize { get; set; } = 100;
    public string UploadCron { get; set; } = "*/10 * * * * *"; // Every 10 sec
    public string BatchCron { get; set; } = "*/15 * * * *"; // Every 15 minutes
    public string BlockchainCron { get; set; } = "*/10 * * * *"; // Every 10 minutes
    public int ConcurrentUploads { get; set; } = 5;
    public int ProcessingTimeoutMinutes { get; set; } = 60;
    public bool DryRunMode { get; set; } = false;
    public string WorkerId { get; set; } = Environment.MachineName;
    public int MaxInvoicesPerRun { get; set; } = 10;
}

public class ObservabilityConfiguration
{
    public const string SectionName = "Observability";

    public string? SentryDsn { get; set; }
    public bool EnableMetrics { get; set; } = true;
    public bool EnableDetailedLogging { get; set; } = false;
    public string LogLevel { get; set; } = "Information";
}