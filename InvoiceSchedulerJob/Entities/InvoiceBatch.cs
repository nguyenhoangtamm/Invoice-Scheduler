using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceSchedulerJob.Entites;

[Table("invoiceBatches")]
public class InvoiceBatch : BaseEntity
{
    [Column("batchId")]
    [MaxLength(100)]
    public string BatchId { get; set; } = string.Empty;

    [Column("count")]
    public int Count { get; set; }

    [Column("merkleRoot")]
    [MaxLength(255)]
    public string? MerkleRoot { get; set; }

    [Column("batchCid")]
    [MaxLength(255)]
    public string? BatchCid { get; set; }

    // Status changed to integer codes
    [Column("status")]
    public int Status { get; set; } = BatchStatus.Processing;

    [Column("txHash")]
    [MaxLength(255)]
    public string? TxHash { get; set; }

    [Column("blockNumber")]
    public long? BlockNumber { get; set; }

    [Column("confirmedAt")]
    public DateTime? ConfirmedAt { get; set; }

    // Navigation properties
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}

// Batch status constants (now integers)
public static class BatchStatus
{
    public const int Processing = 1;        // processing
    public const int ReadyToSend = 2;      // ready_to_send
    public const int BlockchainPending = 3; // blockchain_pending
    public const int BlockchainConfirmed = 4; // blockchain_confirmed
    public const int BlockchainFailed = 5; // blockchain_failed
    public const int Completed = 6;        // completed
}