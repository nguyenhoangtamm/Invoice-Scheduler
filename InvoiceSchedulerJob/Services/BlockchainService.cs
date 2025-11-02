using System.Numerics;
using InvoiceSchedulerJob.Configuration;
using InvoiceSchedulerJob.Services.Interfaces;
using Microsoft.Extensions.Options;
using Nethereum.Contracts;
using Nethereum.Hex.HexTypes;
using Nethereum.RPC.Eth.DTOs;
using Nethereum.Web3;
using Nethereum.Web3.Accounts;
using Polly;
using Polly.Extensions.Http;

namespace InvoiceSchedulerJob.Services;

public class BlockchainService : IBlockchainService
{
    private IWeb3 _web3;
    private readonly BlockchainConfiguration _config;
    private readonly ILogger<BlockchainService> _logger;
    private readonly IAsyncPolicy _retryPolicy;
  private Account? _account;

    // Simple contract ABI for batch submission
    private const string ContractAbi = @"[
        {
            ""inputs"": [
                {""name"": ""merkleRoot"", ""type"": ""bytes32""},
                {""name"": ""batchId"", ""type"": ""string""},
                {""name"": ""metadataCid"", ""type"": ""string""}
            ],
            ""name"": ""submitBatch"",
            ""outputs"": [],
            ""stateMutability"": ""nonpayable"",
            ""type"": ""function""
        },
        {
            ""inputs"": [
                {""name"": ""cidHash"", ""type"": ""bytes32""},
                {""name"": ""invoiceId"", ""type"": ""uint256""}
            ],
            ""name"": ""submitInvoice"",
            ""outputs"": [],
            ""stateMutability"": ""nonpayable"",
            ""type"": ""function""
        }
    ]";

    public BlockchainService(
        IWeb3 web3,
      IOptions<BlockchainConfiguration> config,
        ILogger<BlockchainService> logger)
    {
    _web3 = web3;
    _config = config.Value;
 _logger = logger;
        _retryPolicy = CreateRetryPolicy();

        // Initialize account if private key is provided
      if (!string.IsNullOrEmpty(_config.PrivateKey) && string.IsNullOrEmpty(_config.KmsEndpoint))
        {
            var privateKeyBytes = Convert.FromHexString(_config.PrivateKey.StartsWith("0x") ? _config.PrivateKey[2..] : _config.PrivateKey);
     var chainId = BigInteger.Parse(_config.ChainId);
   _account = new Account(privateKeyBytes, chainId);
 _web3 = new Web3(_account, _config.RpcUrl);
            _logger.LogInformation("Initialized blockchain service with local account");
        }
        else if (!string.IsNullOrEmpty(_config.KmsEndpoint))
{
    _logger.LogInformation("Initialized blockchain service with KMS endpoint: {KmsEndpoint}", _config.KmsEndpoint);
            // TODO: Implement KMS signing
     }
     else
    {
   _logger.LogWarning("No signing method configured (neither private key nor KMS)");
  }
    }

    private IAsyncPolicy CreateRetryPolicy()
    {
        return Policy
            .Handle<Exception>(ex => !(ex is ArgumentException)) // Don't retry on bad arguments
            .WaitAndRetryAsync(
                retryCount: _config.MaxRetries,
                sleepDurationProvider: retryAttempt => TimeSpan.FromMilliseconds(
                    _config.RetryDelayMs * Math.Pow(2, retryAttempt - 1) + Random.Shared.Next(0, 1000)),
                onRetry: (exception, timespan, retryCount, context) =>
                {
                    _logger.LogWarning(
                        "Blockchain operation retry {RetryCount}/{MaxRetries} after {Delay}ms. Reason: {Reason}",
                        retryCount, _config.MaxRetries, timespan.TotalMilliseconds, exception.Message);
                });
    }

    public async Task<string> SubmitBatchAsync(string merkleRoot, string batchId, string? metadataCid, CancellationToken cancellationToken = default)
    {
        if (_account == null)
        {
            throw new InvalidOperationException("No account configured for signing transactions");
        }

        _logger.LogInformation("Submitting batch to blockchain: {BatchId} with merkle root {MerkleRoot}", batchId, merkleRoot);

        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var contract = _web3.Eth.GetContract(ContractAbi, _config.ContractAddress);
            var submitBatchFunction = contract.GetFunction("submitBatch");

            // Convert merkle root to bytes32
            var merkleRootBytes = Convert.FromHexString(merkleRoot.StartsWith("0x") ? merkleRoot[2..] : merkleRoot);

            // Estimate gas
            var gasEstimate = await submitBatchFunction.EstimateGasAsync(
                from: _account.Address,
                gas: null,
                value: null,
                merkleRootBytes,
                batchId,
                metadataCid ?? string.Empty);

            // Add 20% buffer to gas estimate
            var gasLimit = new HexBigInteger(gasEstimate.Value * 120 / 100);

            // Get current gas price
            var gasPrice = await _web3.Eth.GasPrice.SendRequestAsync();
            var maxGasPrice = new HexBigInteger(_config.MaxGasPrice);

            if (gasPrice.Value > maxGasPrice.Value)
            {
                _logger.LogWarning(
                    "Current gas price {CurrentGasPrice} exceeds maximum {MaxGasPrice}, using maximum",
                    gasPrice.Value, maxGasPrice.Value);
                gasPrice = maxGasPrice;
            }

            // Send transaction
            var txHash = await submitBatchFunction.SendTransactionAsync(
                from: _account.Address,
                gas: gasLimit,
                gasPrice: gasPrice,
                value: null,
                merkleRootBytes,
                batchId,
                metadataCid ?? string.Empty);

            _logger.LogInformation("Batch {BatchId} submitted to blockchain with tx hash: {TxHash}", batchId, txHash);
            return txHash;
        });
    }

    public async Task<string> SubmitInvoiceAsync(string cidHash, int invoiceId, CancellationToken cancellationToken = default)
    {
        if (_account == null)
        {
            throw new InvalidOperationException("No account configured for signing transactions");
        }

        _logger.LogInformation("Submitting invoice to blockchain: {InvoiceId} with CID hash {CidHash}", invoiceId, cidHash);

        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var contract = _web3.Eth.GetContract(ContractAbi, _config.ContractAddress);
            var submitInvoiceFunction = contract.GetFunction("submitInvoice");

            // Convert CID hash to bytes32
            var cidHashBytes = Convert.FromHexString(cidHash.StartsWith("0x") ? cidHash[2..] : cidHash);

            // Estimate gas
            var gasEstimate = await submitInvoiceFunction.EstimateGasAsync(
                from: _account.Address,
                gas: null,
                value: null,
                cidHashBytes,
                new BigInteger(invoiceId));

            // Add 20% buffer to gas estimate
            var gasLimit = new HexBigInteger(gasEstimate.Value * 120 / 100);

            // Get current gas price
            var gasPrice = await _web3.Eth.GasPrice.SendRequestAsync();
            var maxGasPrice = new HexBigInteger(_config.MaxGasPrice);

            if (gasPrice.Value > maxGasPrice.Value)
            {
                gasPrice = maxGasPrice;
            }

            // Send transaction
            var txHash = await submitInvoiceFunction.SendTransactionAsync(
                from: _account.Address,
                gas: gasLimit,
                gasPrice: gasPrice,
                value: null,
                cidHashBytes,
                new BigInteger(invoiceId));

            _logger.LogInformation("Invoice {InvoiceId} submitted to blockchain with tx hash: {TxHash}", invoiceId, txHash);
            return txHash;
        });
    }

    public async Task<bool> IsTransactionConfirmedAsync(string txHash, int requiredConfirmations, CancellationToken cancellationToken = default)
    {
        try
        {
            var receipt = await GetTransactionReceiptAsync(txHash, cancellationToken);
            if (receipt == null || receipt.Status?.Value != 1)
            {
                return false;
            }

            var currentBlock = await GetCurrentBlockNumberAsync(cancellationToken);
            var confirmations = currentBlock - (long)receipt.BlockNumber.Value + 1;

            return confirmations >= requiredConfirmations;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to check confirmation status for tx {TxHash}", txHash);
            return false;
        }
    }

    public async Task<TransactionReceipt?> GetTransactionReceiptAsync(string txHash, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _retryPolicy.ExecuteAsync(async () =>
            {
                return await _web3.Eth.Transactions.GetTransactionReceipt.SendRequestAsync(txHash);
            });
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get transaction receipt for {TxHash}", txHash);
            return null;
        }
    }

    public async Task<long> GetCurrentBlockNumberAsync(CancellationToken cancellationToken = default)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var blockNumber = await _web3.Eth.Blocks.GetBlockNumber.SendRequestAsync();
            return (long)blockNumber.Value;
        });
    }
}