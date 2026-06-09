namespace AIWebservice.Models.coa
{
    public sealed class RegNoReviewUpdateResponse
    {
        public string RegNo { get; set; } = string.Empty;
        public bool Success { get; set; } = true;

        public int HeaderRowsAffected { get; set; }

        public int DetailRowsAffected { get; set; }

        public IReadOnlyList<RegNoReviewDetailUpdateResult> ItemResults { get; set; } = [];

        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}