using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceSchedulerJob.Models;

[Table("invoiceLines")]
public class InvoiceLine
{
    [Key]
    [Column("id")]
    public int Id { get; set; }

    [Column("invoiceId")]
    public int InvoiceId { get; set; }

    [Column("lineNumber")]
    public int LineNumber { get; set; }

    [Column("description")]
    [MaxLength(500)]
    public string Description { get; set; } = string.Empty;

    [Column("unit")]
    [MaxLength(50)]
    public string Unit { get; set; } = string.Empty;

    [Column("quantity", TypeName = "decimal(18,4)")]
    public decimal Quantity { get; set; }

    [Column("unitPrice", TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Column("discount", TypeName = "decimal(18,2)")]
    public decimal Discount { get; set; }

    [Column("taxRate", TypeName = "decimal(5,2)")]
    public decimal TaxRate { get; set; }

    [Column("taxAmount", TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column("lineTotal", TypeName = "decimal(18,2)")]
    public decimal LineTotal { get; set; }

    [Column("createdAt")]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updatedAt")]
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    [ForeignKey("InvoiceId")]
    public Invoice Invoice { get; set; } = null!;
}