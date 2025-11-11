namespace InvoiceSchedulerJob.DTOs;

public class VerifyInvoiceResponseDto
{
    public bool IsValid { get; set; }
    public string Message { get; set; } = string.Empty;
    public InvoiceBatchDto? BatchInfo { get; set; }
    public string? MetadataJson { get; set; }
}