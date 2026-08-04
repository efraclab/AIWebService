using System.ComponentModel.DataAnnotations;

namespace AIWebservice.Models.coa
{
    public sealed class RegNoReviewUpdateRequest
    {
        [Required(ErrorMessage = "regNo is required.")]
        [StringLength(100, ErrorMessage = "regNo must be <= 100 characters.")]
        public string RegNo { get; set; } = string.Empty;

        public RegNoReviewHeaderUpdate? Header { get; set; }

        public string? ChangedBy { get; set; }

        public List<RegNoReviewDetailUpdate> Items { get; set; } = new();
    }
}