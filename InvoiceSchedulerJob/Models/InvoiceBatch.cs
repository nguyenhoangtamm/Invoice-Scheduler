using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceSchedulerJob.Models;

[Table("invoiceBatches")]
public class InvoiceBatch
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

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

    [Column("externalBatchId")]
    [MaxLength(100)]
    public string? ExternalBatchId { get; set; }

    [Column("status")]
    [MaxLength(50)]
    public string Status { get; set; } = "processing";

    [Column("txHash")]
    [MaxLength(255)]
    public string? TxHash { get; set; }

    [Column("blockNumber")]
    public long? BlockNumber { get; set; }

    [Column("confirmedAt")]
    public DateTime? ConfirmedAt { get; set; }

    [Column("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    [Column("createdBy")]
    public int? CreatedBy { get; set; }

    [Column("updatedBy")]
    public int? UpdatedBy { get; set; }

    // Navigation properties
    public ICollection<Invoice> Invoices { get; set; } = new List<Invoice>();
}

// Batch status constants
public static class BatchStatus
{
    public const string Processing = "processing";
    public const string ReadyToSend = "ready_to_send";
    public const string BlockchainPending = "blockchain_pending";
    public const string BlockchainConfirmed = "blockchain_confirmed";
    public const string BlockchainFailed = "blockchain_failed";
    public const string Completed = "completed";
}