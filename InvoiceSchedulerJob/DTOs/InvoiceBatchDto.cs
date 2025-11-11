namespace InvoiceSchedulerJob.DTOs;

public class InvoiceBatchDto
{
    public string MerkleRoot { get; set; } = string.Empty;
    public int BatchSize { get; set; }
    public string Issuer { get; set; } = string.Empty;
    public string MetadataUri { get; set; } = string.Empty;
    public long Timestamp { get; set; }
}