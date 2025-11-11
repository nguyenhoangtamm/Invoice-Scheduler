namespace InvoiceSchedulerJob.DTOs;

public class VerifyInvoiceRequestDto
{
    public string MerkleRoot { get; set; } = string.Empty;
    public string InvoiceCid { get; set; } = string.Empty;
    public string[] MerkleProof { get; set; } = Array.Empty<string>();
}