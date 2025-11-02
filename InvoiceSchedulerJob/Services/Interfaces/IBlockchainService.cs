using Nethereum.Web3;
using Nethereum.Contracts;
using Nethereum.RPC.Eth.DTOs;

namespace InvoiceSchedulerJob.Services.Interfaces;

public interface IBlockchainService
{
    Task<string> SubmitBatchAsync(string merkleRoot, string batchId, string? metadataCid, CancellationToken cancellationToken = default);
    Task<string> SubmitInvoiceAsync(string cidHash, int invoiceId, CancellationToken cancellationToken = default);
    Task<bool> IsTransactionConfirmedAsync(string txHash, int requiredConfirmations, CancellationToken cancellationToken = default);
    Task<TransactionReceipt?> GetTransactionReceiptAsync(string txHash, CancellationToken cancellationToken = default);
    Task<long> GetCurrentBlockNumberAsync(CancellationToken cancellationToken = default);
}