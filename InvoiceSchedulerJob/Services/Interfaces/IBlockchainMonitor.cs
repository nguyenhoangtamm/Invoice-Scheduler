using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InvoiceSchedulerJob.Services.Interfaces;
public interface IBlockchainMonitor
{
    Task MonitorLatestBlockAsync();
    Task CheckPendingTransactionsAsync();
    Task ProcessBlockTransactionsAsync(ulong blockNumber);
}

