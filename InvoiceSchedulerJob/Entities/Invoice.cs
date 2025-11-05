using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceSchedulerJob.Entites;

[Table("invoices")]
public class Invoice : BaseEntity
{
    [Column("invoiceNumber")]
    [MaxLength(100)]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Column("formNumber")]
    [MaxLength(50)]
    public string FormNumber { get; set; } = string.Empty;

    [Column("serial")]
    [MaxLength(50)]
    public string Serial { get; set; } = string.Empty;

    // Tenant organization (foreign key to organizations.id)
    [Column("tenantOrganizationId")]
    public int TenantOrganizationId { get; set; }

    // Issued by user (foreign key to users.id)
    [Column("issuedByUserId")]
    public int IssuedByUserId { get; set; }

    [Column("sellerName")]
    [MaxLength(255)]
    public string SellerName { get; set; } = string.Empty;

    [Column("sellerTaxId")]
    [MaxLength(50)]
    public string SellerTaxId { get; set; } = string.Empty;

    [Column("sellerAddress")]
    [MaxLength(500)]
    public string SellerAddress { get; set; } = string.Empty;

    [Column("sellerPhone")]
    [MaxLength(20)]
    public string SellerPhone { get; set; } = string.Empty;

    [Column("sellerEmail")]
    [MaxLength(100)]
    public string SellerEmail { get; set; } = string.Empty;

    [Column("customerName")]
    [MaxLength(255)]
    public string CustomerName { get; set; } = string.Empty;

    [Column("customerTaxId")]
    [MaxLength(50)]
    public string CustomerTaxId { get; set; } = string.Empty;

    [Column("customerAddress")]
    [MaxLength(500)]
    public string CustomerAddress { get; set; } = string.Empty;

    [Column("customerPhone")]
    [MaxLength(20)]
    public string CustomerPhone { get; set; } = string.Empty;

    [Column("customerEmail")]
    [MaxLength(100)]
    public string CustomerEmail { get; set; } = string.Empty;

    [Column("status")]
    public int Status { get; set; }

    // Issued date (column name in DB is issuedDate)
    [Column("issuedDate")]
    public DateTime IssueDate { get; set; }

    [Column("subTotal", TypeName = "decimal(18,2)")]
    public decimal SubTotal { get; set; }

    [Column("taxAmount", TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column("discountAmount", TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; }

    [Column("totalAmount", TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    [Column("currency")]
    [MaxLength(3)]
    public string Currency { get; set; } = "VND";

    [Column("note")]
    public string? Note { get; set; }

    [Column("batchId")]
    public int? BatchId { get; set; }

    [Column("immutableHash")]
    [MaxLength(255)]
    public string? ImmutableHash { get; set; }

    [Column("cid")]
    [MaxLength(255)]
    public string? Cid { get; set; }

    [Column("cidHash")]
    [MaxLength(255)]
    public string? CidHash { get; set; }

    [Column("merkleProof")]
    public string? MerkleProof { get; set; }

    // Navigation properties
    [ForeignKey("BatchId")]
    public InvoiceBatch? Batch { get; set; }

    public ICollection<InvoiceLine> Lines { get; set; } = new List<InvoiceLine>();
}

// Invoice status constants
public static class InvoiceStatus
{
    public const int Uploaded = 1;           // Đã upload
    public const int IpfsStored = 2;         // Đã lưu trên IPFS
    public const int IpfsFailed = 3;         // Upload IPFS thất bại
    public const int Batched = 4;            // Đã tạo batch
    public const int BlockchainPending = 5;  // Chờ xác nhận blockchain
    public const int BlockchainConfirmed = 6; // Đã xác nhận trên blockchain
    public const int BlockchainFailed = 7;   // Ghi blockchain thất bại
    public const int Finalized = 8;          // Hoàn tất
    public const int Archived = 9;           // Đã lưu trữ
}