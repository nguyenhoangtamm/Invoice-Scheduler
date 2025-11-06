using Hangfire;
using Hangfire.PostgreSql;
using InvoiceSchedulerJob.Data;
using InvoiceSchedulerJob.Services;
using InvoiceSchedulerJob.Services.Interfaces;
using Microsoft.EntityFrameworkCore;
using Nethereum.Web3;

namespace InvoiceSchedulerJob.Configuration;

public static class ServiceCollectionExtensions
{
    /// <summary>
    /// Registers all application services and configurations
    /// </summary>
    public static IServiceCollection AddApplicationServices(
   this IServiceCollection services,
        IConfiguration configuration)
    {
        // Configure options
        services.Configure<IpfsConfiguration>(
      configuration.GetSection(IpfsConfiguration.SectionName));
        services.Configure<BlockchainConfiguration>(
            configuration.GetSection(BlockchainConfiguration.SectionName));
        services.Configure<JobConfiguration>(
     configuration.GetSection(JobConfiguration.SectionName));
        services.Configure<ObservabilityConfiguration>(
      configuration.GetSection(ObservabilityConfiguration.SectionName));

        return services;
    }

    /// <summary>
    /// Registers database context
    /// </summary>
    public static IServiceCollection AddDatabaseContext(
        this IServiceCollection services,
        IConfiguration configuration)
    {
        services.AddDbContext<InvoiceDbContext>(options =>
  options.UseNpgsql(configuration.GetConnectionString("DefaultConnection")));

        return services;
    }

    /// <summary>
    /// Registers Hangfire storage and server
    /// </summary>
    public static IServiceCollection AddHangfireServices(
        this IServiceCollection services,
      IConfiguration configuration)
    {
        var hangfireStorageOptions = new PostgreSqlStorageOptions
        {
            DistributedLockTimeout = TimeSpan.FromMinutes(5)
        };

        services.AddHangfire(config =>
            config.UsePostgreSqlStorage(
    configuration.GetConnectionString("HangfireConnection"),
         hangfireStorageOptions));

        services.AddHangfireServer(options =>
        {
            options.Queues = new[] { "default", "batch", "blockchain" };
        });

        return services;
    }

    /// <summary>
    /// Registers blockchain services
    /// </summary>
    public static IServiceCollection AddBlockchainServices(
        this IServiceCollection services,
 IConfiguration configuration)
    {
        // Register IWeb3
        services.AddSingleton<IWeb3>(sp =>
        {
            var config = sp.GetRequiredService<Microsoft.Extensions.Options.IOptions<BlockchainConfiguration>>().Value;
            return new Web3(config.RpcUrl);
        });

        services.AddScoped<IBlockchainService, BlockchainService>();
        services.AddScoped<MerkleTreeService>();
        services.AddSingleton<MetricsService>();

        // Legacy services
        services.AddSingleton<IEthereumJobService, EthereumJobService>();
        services.AddSingleton<IBlockchainMonitor, BlockchainMonitor>();

        return services;
    }

    /// <summary>
    /// Registers job services
    /// </summary>
    public static IServiceCollection AddJobServices(
      this IServiceCollection services)
    {
        services.AddScoped<IUploadToIpfsJob, UploadToIpfsJob>();
        services.AddScoped<ICreateBatchJob, CreateBatchJob>();
        services.AddScoped<ISubmitToBlockchainJob, SubmitToBlockchainJob>();

        return services;
    }

    /// <summary>
    /// Registers HTTP clients with resilience policies
    /// </summary>
    public static IServiceCollection AddHttpClients(
        this IServiceCollection services)
    {
        services.AddHttpClient<IIpfsService, IpfsService>();

        return services;
    }
}
