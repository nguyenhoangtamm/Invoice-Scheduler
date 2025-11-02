using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace InvoiceSchedulerJob.DTOs;
public class TransactionReceiptDto
{
    public string TransactionHash { get; set; }
    public string BlockNumber { get; set; }
    public string Status { get; set; }
    public string GasUsed { get; set; }
}