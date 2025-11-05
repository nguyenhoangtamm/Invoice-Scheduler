using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceSchedulerJob.Entites;

public abstract class BaseEntity
{
    [Key]
    [Column("id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Required]
    public int Id { get; set; }

    [Column("createdBy")]
    public int? CreatedBy { get; set; }

    [Column("createdAt")]
    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("updatedBy")]
    public int? UpdatedBy { get; set; }

    [Column("updatedAt")]
    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;

    //[Column("rowVersion")]
    //[Timestamp]
    //[ConcurrencyCheck]
    //public uint RowVersion { get; set; }
}