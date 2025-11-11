using Nethereum.Web3;
using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;
using InvoiceSchedulerJob.DTOs;

namespace InvoiceSchedulerJob.Services.Interfaces;

public interface IBlockchainService
{
    Task<string> SubmitBatchAsync(string merkleRoot, int batchSize, string? metadataUri, CancellationToken cancellationToken = default);
    Task<bool> VerifyInvoiceAsync(string merkleRoot, string invoiceCid, byte[][] merkleProof, CancellationToken cancellationToken = default);
    Task RegisterIndividualInvoiceAsync(string merkleRoot, string invoiceId, string invoiceCid, string invoiceHash, CancellationToken cancellationToken = default);
    Task<InvoiceBatchDto?> GetBatchAsync(string merkleRoot, CancellationToken cancellationToken = default);
    Task<bool> IsTransactionConfirmedAsync(string txHash, int requiredConfirmations, CancellationToken cancellationToken = default);
    Task<TransactionReceipt?> GetTransactionReceiptAsync(string txHash, CancellationToken cancellationToken = default);
    Task<long> GetCurrentBlockNumberAsync(CancellationToken cancellationToken = default);
}