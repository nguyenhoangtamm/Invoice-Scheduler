using Microsoft.EntityFrameworkCore;
using InvoiceSchedulerJob.Models;

namespace InvoiceSchedulerJob.Data;

public class InvoiceDbContext : DbContext
{
    public InvoiceDbContext(DbContextOptions<InvoiceDbContext> options) : base(options)
    {
    }

    public DbSet<Invoice> Invoices { get; set; }
    public DbSet<InvoiceBatch> InvoiceBatches { get; set; }
    public DbSet<InvoiceLine> InvoiceLines { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Configure Invoice entity
        modelBuilder.Entity<Invoice>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Configure relationships
            entity.HasOne(i => i.Batch)
                  .WithMany(b => b.Invoices)
                  .HasForeignKey(i => i.BatchId)
                  .OnDelete(DeleteBehavior.SetNull);

            entity.HasMany(i => i.Lines)
                  .WithOne(l => l.Invoice)
                  .HasForeignKey(l => l.InvoiceId)
                  .OnDelete(DeleteBehavior.Cascade);

            // Indexes for performance
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.Cid);
            entity.HasIndex(e => e.BatchId);
            entity.HasIndex(e => e.CreatedAt);
            entity.HasIndex(e => new { e.Status, e.CreatedAt });
        });

        // Configure InvoiceBatch entity
        modelBuilder.Entity<InvoiceBatch>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Indexes
            entity.HasIndex(e => e.BatchId).IsUnique();
            entity.HasIndex(e => e.Status);
            entity.HasIndex(e => e.TxHash);
        });

        // Configure InvoiceLine entity
        modelBuilder.Entity<InvoiceLine>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(e => e.UpdatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            // Indexes
            entity.HasIndex(e => e.InvoiceId);
            entity.HasIndex(e => new { e.InvoiceId, e.LineNumber }).IsUnique();
        });
    }
}