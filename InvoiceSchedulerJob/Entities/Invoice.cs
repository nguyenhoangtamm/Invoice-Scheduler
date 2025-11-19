using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceSchedulerJob.Entites;

[Table("Invoices")]
public class Invoice : BaseEntity
{
    [Column("InvoiceNumber")]
    [MaxLength(100)]
    public string InvoiceNumber { get; set; } = string.Empty;

    [Column("FormNumber")]
    [MaxLength(50)]
    public string FormNumber { get; set; } = string.Empty;

    [Column("Serial")]
    [MaxLength(50)]
    public string Serial { get; set; } = string.Empty;

    // Tenant organization (foreign key to organizations.id)
    [Column("OrganizationId")]
    public int TenantOrganizationId { get; set; }

    // Issued by user (foreign key to users.id)
    [Column("IssuedByUserId")]
    public int IssuedByUserId { get; set; }

    [Column("SellerName")]
    [MaxLength(255)]
    public string SellerName { get; set; } = string.Empty;

    [Column("SellerTaxId")]
    [MaxLength(50)]
    public string SellerTaxId { get; set; } = string.Empty;

    [Column("SellerAddress")]
    [MaxLength(500)]
    public string SellerAddress { get; set; } = string.Empty;

    [Column("SellerPhone")]
    [MaxLength(20)]
    public string SellerPhone { get; set; } = string.Empty;

    [Column("SellerEmail")]
    [MaxLength(100)]
    public string SellerEmail { get; set; } = string.Empty;

    [Column("CustomerName")]
    [MaxLength(255)]
    public string CustomerName { get; set; } = string.Empty;

    [Column("CustomerTaxId")]
    [MaxLength(50)]
    public string CustomerTaxId { get; set; } = string.Empty;

    [Column("CustomerAddress")]
    [MaxLength(500)]
    public string CustomerAddress { get; set; } = string.Empty;

    [Column("CustomerPhone")]
    [MaxLength(20)]
    public string CustomerPhone { get; set; } = string.Empty;

    [Column("CustomerEmail")]
    [MaxLength(100)]
    public string CustomerEmail { get; set; } = string.Empty;

    [Column("Status")]
    public int Status { get; set; }

    // Issued date (column name in DB is IssuedDate)
    [Column("IssuedDate")]
    public DateTime IssueDate { get; set; }

    [Column("SubTotal", TypeName = "decimal(18,2)")]
    public decimal SubTotal { get; set; }

    [Column("TaxAmount", TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column("DiscountAmount", TypeName = "decimal(18,2)")]
    public decimal DiscountAmount { get; set; }

    [Column("TotalAmount", TypeName = "decimal(18,2)")]
    public decimal TotalAmount { get; set; }

    [Column("Currency")]
    [MaxLength(3)]
    public string Currency { get; set; } = "VND";

    [Column("Note")]
    public string? Note { get; set; }

    [Column("BatchId")]
    public int? BatchId { get; set; }

    [Column("ImmutableHash")]
    [MaxLength(255)]
    public string? ImmutableHash { get; set; }

    [Column("Cid")]
    [MaxLength(255)]
    public string? Cid { get; set; }

    [Column("CidHash")]
    [MaxLength(255)]
    public string? CidHash { get; set; }

    [Column("MerkleProof")]
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
    public const int Batched = 3;            // Đã tạo batch
    public const int BlockchainConfirmed = 4; // Đã xác nhận trên blockchain
    public const int Finalized = 5;          // Hoàn tất
    public const int IpfsFailed = 101;         // Upload IPFS thất bại
    public const int BlockchainFailed = 102;   // Ghi blockchain thất bại
}