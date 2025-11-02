using Nethereum.RPC.Eth.DTOs;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using InvoiceSchedulerJob.DTOs;

namespace InvoiceSchedulerJob.Services.Interfaces;

public interface IEthereumJobService
{
    Task<string> SendTransactionAsync(string toAddress, decimal amount);
    Task<string> GetBalanceAsync(string address);
    Task<TransactionReceiptDto> WaitForTransactionReceiptAsync(string transactionHash);
    Task<string> CallSmartContractReadAsync(string contractAddress, string abi, string functionName, params object[] parameters);
    Task<string> CallSmartContractWriteAsync(string contractAddress, string abi, string functionName, params object[] parameters);
}
