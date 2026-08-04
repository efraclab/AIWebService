namespace AIWebservice.Models.coa
{
    public sealed class RegNoReviewSetApprovalRequest
    {
        public string RegNo { get; set; } = string.Empty;

        public string Status { get; set; } = string.Empty;

        public string ReviewedBy { get; set; } = string.Empty;

        public string? Notes { get; set; }
    }
}