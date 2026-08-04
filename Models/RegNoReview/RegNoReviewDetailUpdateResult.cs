namespace AIWebservice.Models.coa
{
    public sealed class RegNoReviewDetailUpdateResult
    {
        public string GroupCode { get; set; } = string.Empty;
        public string Parameter { get; set; } = string.Empty;

        public int RowsAffected { get; set; }

        public bool Skipped { get; set; }

        public string? SkipReason { get; set; }
    }
}