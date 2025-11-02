using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using InvoiceSchedulerJob.Configuration;
using InvoiceSchedulerJob.Services.Interfaces;
using Microsoft.Extensions.Options;
using Pinata.Client;
using Polly;
using Polly.Extensions.Http;

namespace InvoiceSchedulerJob.Services;

public class IpfsService : IIpfsService
{
    private readonly IPinataClient _pinataClient;
    private readonly IpfsConfiguration _config;
    private readonly ILogger<IpfsService> _logger;
    private readonly SemaphoreSlim _rateLimitSemaphore;
    private readonly IAsyncPolicy<HttpResponseMessage> _retryPolicy;
    private readonly HttpClient _httpClient;

    public IpfsService(
        HttpClient httpClient,
        IOptions<IpfsConfiguration> config,
        ILogger<IpfsService> logger)
    {
        _httpClient = httpClient;
        _config = config.Value;
        _logger = logger;
        _rateLimitSemaphore = new SemaphoreSlim(_config.RateLimitPerMinute, _config.RateLimitPerMinute);
        // Initialize Pinata client using the HTTP client
        _pinataClient = new PinataClient(new Config
        {
            ApiKey = _config.ApiKey,
            ApiSecret = _config.ApiSecret
        });
        // Test the Pinata client initialization

        ConfigureHttpClient();
        _retryPolicy = CreateRetryPolicy();
    }

    private void ConfigureHttpClient()
    {
        _httpClient.BaseAddress = new Uri(_config.BaseUrl);
        _httpClient.Timeout = TimeSpan.FromMilliseconds(_config.TimeoutMs);
        // Use Bearer token with JWT authentication (Pinata v3 API)
        _httpClient.DefaultRequestHeaders.Authorization =
 new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", _config.ApiKey);
    }

    private IAsyncPolicy<HttpResponseMessage> CreateRetryPolicy()
    {
        return HttpPolicyExtensions
            .HandleTransientHttpError()
            .OrResult(msg => !msg.IsSuccessStatusCode && msg.StatusCode != System.Net.HttpStatusCode.BadRequest)
            .WaitAndRetryAsync(
     retryCount: _config.MaxRetries,
            sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(
    _config.RetryDelayMs * Math.Pow(2, retryAttempt - 1) + Random.Shared.Next(0, 1000)),
       onRetry: (outcome, timespan, retryCount, context) =>
  {
      _logger.LogWarning(
           "IPFS request retry {RetryCount}/{MaxRetries} after {Delay}ms. Reason: {Reason}",
         retryCount, _config.MaxRetries, timespan.TotalMilliseconds,
                outcome.Exception?.Message ?? outcome.Result?.StatusCode.ToString());
  });
    }

    public async Task<string> PinJsonAsync(object data, string fileName, CancellationToken cancellationToken = default)
    {
        var json = JsonSerializer.Serialize(data, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

        return await PinJsonAsync(json, fileName, cancellationToken);
    }

    public async Task<string> PinJsonAsync(string json, string fileName, CancellationToken cancellationToken = default)
    {
        await _rateLimitSemaphore.WaitAsync(cancellationToken);

        try
        {
            _logger.LogInformation("Pinning JSON to IPFS via Pinata: {FileName} ({Size} bytes)", fileName, json.Length);

            // Create Pinata metadata
            var metadata = new Dictionary<string, string>
          {
         { "timestamp", DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString() },
             { "size", json.Length.ToString() }
   };

            // Use Pinata client to pin JSON with retry policy
            string? cid = null;
            var attempts = 0;

            while (attempts < _config.MaxRetries && string.IsNullOrEmpty(cid))
            {
                try
                {
                    var response = await _pinataClient.Pinning.PinJsonToIpfsAsync(json, new Pinata.Client.PinataMetadata { Name = fileName, KeyValues = metadata }, new Pinata.Client.PinataOptions());

                    cid = response.IpfsHash;
                    break;
                }
                catch (Exception ex)
                {
                    attempts++;
                    if (attempts >= _config.MaxRetries)
                    {
                        _logger.LogError(ex, "Error pinning JSON to IPFS via Pinata after {Attempts} attempts", attempts);
                        throw;
                    }
                    _logger.LogWarning(ex, "Error pinning JSON to IPFS, attempt {Attempt}/{MaxRetries}", attempts, _config.MaxRetries);
                    var delay = _config.RetryDelayMs * Math.Pow(2, attempts - 1) + Random.Shared.Next(0, 1000);
                    await Task.Delay((int)delay, cancellationToken);
                }
            }

            if (string.IsNullOrEmpty(cid))
            {
                throw new InvalidOperationException("Failed to pin JSON to IPFS - no CID returned");
            }

            _logger.LogInformation("Successfully pinned to IPFS: {FileName} -> {CID}", fileName, cid);
            return cid;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to pin JSON to IPFS: {FileName}", fileName);
            throw;
        }
        finally
        {
            // Release rate limit after a delay
            _ = Task.Delay(TimeSpan.FromMinutes(1.0 / _config.RateLimitPerMinute), cancellationToken)
        .ContinueWith(_ => _rateLimitSemaphore.Release(), cancellationToken);
        }
    }

    public async Task<bool> IsPinnedAsync(string cid, CancellationToken cancellationToken = default)
    {
        try
        {
            await _rateLimitSemaphore.WaitAsync(cancellationToken);

            _logger.LogInformation("Checking if CID is pinned: {CID}", cid);

            var response = await _retryPolicy.ExecuteAsync(async () =>
        {
            return await _httpClient.GetAsync($"/data/pinList?hashContains={cid}", cancellationToken);
        });

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning("Failed to check pin status, status code: {Status}", response.StatusCode);
                return false;
            }

            var responseJson = await response.Content.ReadAsStringAsync(cancellationToken);
            var result = JsonSerializer.Deserialize<JsonElement>(responseJson);

            var rows = result.GetProperty("rows");
            var isPinned = rows.GetArrayLength() > 0;

            _logger.LogInformation("CID {CID} pinned status: {IsPinned}", cid, isPinned);
            return isPinned;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check if CID is pinned: {CID}", cid);
            return false;
        }
        finally
        {
            _ = Task.Delay(TimeSpan.FromMinutes(1.0 / _config.RateLimitPerMinute), cancellationToken)
           .ContinueWith(_ => _rateLimitSemaphore.Release(), cancellationToken);
        }
    }

    public async Task<string?> GetJsonAsync(string cid, CancellationToken cancellationToken = default)
    {
        try
        {
            await _rateLimitSemaphore.WaitAsync(cancellationToken);

            _logger.LogInformation("Retrieving JSON from IPFS: {CID}", cid);

            // Construct the Pinata gateway URL
            var gatewayUrl = $"{_config.GatewayUrl}/ipfs/{cid}";

            var response = await _retryPolicy.ExecuteAsync(async () =>
            {
                using var gatewayClient = new HttpClient
                {
                    Timeout = TimeSpan.FromMilliseconds(_config.TimeoutMs)
                };

                return await gatewayClient.GetAsync(gatewayUrl, cancellationToken);
            });

            if (!response.IsSuccessStatusCode)
            {
                _logger.LogWarning(
            "Failed to retrieve JSON from IPFS gateway: {CID}, Status: {Status}",
       cid, response.StatusCode);
                return null;
            }

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            _logger.LogInformation("Successfully retrieved JSON from IPFS: {CID}", cid);
            return json;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to retrieve JSON from IPFS: {CID}", cid);
            return null;
        }
        finally
        {
            _ = Task.Delay(TimeSpan.FromMinutes(1.0 / _config.RateLimitPerMinute), cancellationToken)
        .ContinueWith(_ => _rateLimitSemaphore.Release(), cancellationToken);
        }
    }

    public static string ComputeHash(string input)
    {
        using var sha256 = SHA256.Create();
        var hashBytes = sha256.ComputeHash(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(hashBytes).ToLowerInvariant();
    }
}