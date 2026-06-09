namespace AIWebservice.Models
{
    public sealed class PdfReviewResponse
    {
        public string CorrelationId { get; init; } = string.Empty;
        public bool Success { get; init; } = true;
        public string Review { get; init; } = string.Empty;
        public IReadOnlyList<UploadedPdfInfo> Files { get; init; } = [];
        public TokenUsage Usage { get; init; } = new();
        public string Model { get; init; } = string.Empty;
        public DateTimeOffset ProcessedAt { get; init; } = DateTimeOffset.UtcNow;
    }

    public sealed class UploadedPdfInfo
    {
        public string FileId { get; init; } = string.Empty;
        public string Filename { get; init; } = string.Empty;
        public long SizeBytes { get; init; }
        public bool Deleted { get; init; }
    }
}
