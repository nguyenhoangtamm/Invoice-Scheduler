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

    // Contract ABI for Invoice4.sol - anchorBatch function
    private const string ContractAbi = @"[
        {
            ""inputs"": [
                {""name"": ""_merkleRoot"", ""type"": ""bytes32""},
                {""name"": ""_batchSize"", ""type"": ""uint256""},
                {""name"": ""_metadataURI"", ""type"": ""string""}
            ],
            ""name"": ""anchorBatch"",
            ""outputs"": [],
            ""stateMutability"": ""nonpayable"",
            ""type"": ""function""
        },
        {
            ""inputs"": [
                {""name"": ""_merkleRoot"", ""type"": ""bytes32""},
                {""name"": ""_invoiceCID"", ""type"": ""string""},
                {""name"": ""_proof"", ""type"": ""bytes32[1]""}
            ],
            ""name"": ""verifyInvoiceByCID"",
            ""outputs"": [{""name"": """", ""type"": ""bool""}],
            ""stateMutability"": ""nonpayable"",
            ""type"": ""function""
        },
        {
            ""inputs"": [
                {""name"": ""_merkleRoot"", ""type"": ""bytes32""},
                {""name"": ""_invoiceId"", ""type"": ""string""},
                {""name"": ""_invoiceCID"", ""type"": ""string""},
                {""name"": ""_invoiceHash"", ""type"": ""bytes32""}
            ],
            ""name"": ""registerIndividualInvoice"",
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

    public async Task<string> SubmitBatchAsync(string merkleRoot, int batchSize, string? metadataUri, CancellationToken cancellationToken = default)
    {
        if (_account == null)
        {
            throw new InvalidOperationException("No account configured for signing transactions");
        }

        _logger.LogInformation("Anchoring batch to blockchain: merkle root {MerkleRoot}, batch size {BatchSize}, metadata URI {MetadataUri}",
            merkleRoot, batchSize, metadataUri);

        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var contract = _web3.Eth.GetContract(ContractAbi, _config.ContractAddress);
            var anchorBatchFunction = contract.GetFunction("anchorBatch");

            // Convert merkle root to bytes32
            var merkleRootBytes = Convert.FromHexString(merkleRoot.StartsWith("0x") ? merkleRoot[2..] : merkleRoot);

            // Estimate gas
            var gasEstimate = await anchorBatchFunction.EstimateGasAsync(
                from: _account.Address,
                gas: null,
                value: null,
                merkleRootBytes,
                new BigInteger(batchSize),
                metadataUri ?? string.Empty);

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
            var txHash = await anchorBatchFunction.SendTransactionAsync(
                from: _account.Address,
                gas: gasLimit,
                gasPrice: gasPrice,
                value: null,
                merkleRootBytes,
                new BigInteger(batchSize),
                metadataUri ?? string.Empty);

            _logger.LogInformation("Batch anchored to blockchain with tx hash: {TxHash}", txHash);
            return txHash;
        });
    }

    public async Task<bool> VerifyInvoiceAsync(string merkleRoot, string invoiceCid, byte[][] merkleProof, CancellationToken cancellationToken = default)
    {
        if (_account == null)
        {
            throw new InvalidOperationException("No account configured for signing transactions");
        }

        _logger.LogInformation("Verifying invoice on blockchain: CID {InvoiceCid}, merkle root {MerkleRoot}",
            invoiceCid, merkleRoot);

        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var contract = _web3.Eth.GetContract(ContractAbi, _config.ContractAddress);
            var verifyFunction = contract.GetFunction("verifyInvoiceByCID");

            // Convert merkle root to bytes32
            var merkleRootBytes = Convert.FromHexString(merkleRoot.StartsWith("0x") ? merkleRoot[2..] : merkleRoot);

            // Convert merkle proof array
            var proofArray = merkleProof.Select(p => new System.Numerics.BigInteger(p)).ToArray();

            // Estimate gas
            var gasEstimate = await verifyFunction.EstimateGasAsync(
                from: _account.Address,
                gas: null,
                value: null,
                merkleRootBytes,
                invoiceCid,
                proofArray);

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
            var result = await verifyFunction.CallAsync<bool>(
                merkleRootBytes,
                invoiceCid,
                proofArray);

            _logger.LogInformation("Invoice {InvoiceCid} verified successfully", invoiceCid);
            return result;
        });
    }

    public async Task RegisterIndividualInvoiceAsync(string merkleRoot, string invoiceId, string invoiceCid, string invoiceHash, CancellationToken cancellationToken = default)
    {
        if (_account == null)
        {
            throw new InvalidOperationException("No account configured for signing transactions");
        }

        _logger.LogInformation("Registering individual invoice: ID {InvoiceId}, CID {InvoiceCid}, merkle root {MerkleRoot}",
            invoiceId, invoiceCid, merkleRoot);

        await _retryPolicy.ExecuteAsync(async () =>
        {
            var contract = _web3.Eth.GetContract(ContractAbi, _config.ContractAddress);
            var registerFunction = contract.GetFunction("registerIndividualInvoice");

            // Convert merkle root to bytes32
            var merkleRootBytes = Convert.FromHexString(merkleRoot.StartsWith("0x") ? merkleRoot[2..] : merkleRoot);

            // Convert invoice hash to bytes32
            var invoiceHashBytes = Convert.FromHexString(invoiceHash.StartsWith("0x") ? invoiceHash[2..] : invoiceHash);

            // Estimate gas
            var gasEstimate = await registerFunction.EstimateGasAsync(
                from: _account.Address,
                gas: null,
                value: null,
                merkleRootBytes,
                invoiceId,
                invoiceCid,
                invoiceHashBytes);

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
            var txHash = await registerFunction.SendTransactionAsync(
                from: _account.Address,
                gas: gasLimit,
                gasPrice: gasPrice,
                value: null,
                merkleRootBytes,
                invoiceId,
                invoiceCid,
                invoiceHashBytes);

            _logger.LogInformation("Individual invoice {InvoiceId} registered with tx hash: {TxHash}", invoiceId, txHash);
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