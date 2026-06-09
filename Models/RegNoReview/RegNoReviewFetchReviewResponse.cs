namespace AIWebservice.Models.coa
{
    public sealed class RegNoReviewFetchReviewResponse
    {
        public string CorrelationId { get; set; } = string.Empty;
        public bool Success { get; set; } = true;
        public string RegNo { get; set; } = string.Empty;
        public int RowCount { get; set; }
        public IReadOnlyList<RegNoReviewReportRow> Data { get; set; } = [];
        public string Review { get; set; } = string.Empty;
        public TokenUsage Usage { get; set; } = new();
        public string Model { get; set; } = string.Empty;
        public DateTimeOffset ProcessedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}