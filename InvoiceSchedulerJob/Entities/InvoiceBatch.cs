using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceSchedulerJob.Entites;

[Table("InvoiceBatches")]
public class InvoiceBatch : BaseEntity
{
    [Column("BatchId")]
    [MaxLength(100)]
    public string BatchId { get; set; } = string.Empty;

    [Column("Count")]
    public int Count { get; set; }

    [Column("MerkleRoot")]
    [MaxLength(255)]
    public string? MerkleRoot { get; set; }

    [Column("BatchCid")]
    [MaxLength(255)]
    public string? BatchCid { get; set; }

    // Status changed to integer codes
    [Column("Status")]
    public int Status { get; set; } = BatchStatus.Initial;

    [Column("TxHash")]
    [MaxLength(255)]
    public string? TxHash { get; set; }

    [Column("BlockNumber")]
    public long? BlockNumber { get; set; }

    [Column("ConfirmedAt")]
    public DateTime? ConfirmedAt { get; set; }

    // Navigation properties
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}

// Batch status constants (now integers)
public static class BatchStatus
{
    public const int Initial = 1; // initial
    public const int BlockchainConfirmed = 2; // blockchain_confirmed
    public const int BlockchainFailed = 101; // blockchain_failed
}