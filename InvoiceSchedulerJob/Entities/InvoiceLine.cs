using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceSchedulerJob.Entites;

[Table("InvoiceLines")]
public class InvoiceLine : BaseEntity
{
    //[Key]
    //[Column("id")]
    //public int Id { get; set; }

    [Column("InvoiceId")]
    public int InvoiceId { get; set; }

    [Column("LineNumber")]
    public int LineNumber { get; set; }

    [Column("Name")]
    [MaxLength(500)]
    public string Name { get; set; } = string.Empty;

    [Column("Unit")]
    [MaxLength(50)]
    public string Unit { get; set; } = string.Empty;

    [Column("Quantity", TypeName = "decimal(18,4)")]
    public decimal Quantity { get; set; }

    [Column("UnitPrice", TypeName = "decimal(18,2)")]
    public decimal UnitPrice { get; set; }

    [Column("Discount", TypeName = "decimal(18,2)")]
    public decimal Discount { get; set; }

    [Column("TaxRate", TypeName = "decimal(5,2)")]
    public decimal TaxRate { get; set; }

    [Column("TaxAmount", TypeName = "decimal(18,2)")]
    public decimal TaxAmount { get; set; }

    [Column("LineTotal", TypeName = "decimal(18,2)")]
    public decimal LineTotal { get; set; }

    // Navigation properties
    [ForeignKey("InvoiceId")]
    public Invoice Invoice { get; set; } = null!;
}