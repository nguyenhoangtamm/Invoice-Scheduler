using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InvoiceSchedulerJob.Entites;

public abstract class BaseEntity
{
    [Key]
    [Column("Id")]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Required]
    public int Id { get; set; }

    [Column("CreatedBy")]
    public int? CreatedBy { get; set; }

    [Column("CreatedDate")]
    [Required]
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    [Column("UpdatedBy")]
    public int? UpdatedBy { get; set; }

    [Column("UpdatedDate")]
    public DateTime? UpdatedAt { get; set; } = DateTime.UtcNow;

    //[Column("rowVersion")]
    //[Timestamp]
    //[ConcurrencyCheck]
    //public uint RowVersion { get; set; }
}