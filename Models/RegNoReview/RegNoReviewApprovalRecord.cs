namespace AIWebservice.Models.coa
{
    public sealed class RegNoReviewApprovalRecord
    {
        public int Id { get; set; }
        public string RegNo { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string? ReviewedBy { get; set; }
        public DateTime? ReviewedAt { get; set; }
        public DateTime CreatedAt { get; set; }
        public DateTime UpdatedAt { get; set; }
    }
}