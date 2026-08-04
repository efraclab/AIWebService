using System.ComponentModel.DataAnnotations;

namespace AIWebservice.Models.coa
{
    public sealed class RegNoFetchReviewRequest
    {
        [Required(ErrorMessage = "regNo is required.")]
        [StringLength(100, ErrorMessage = "regNo must be <= 100 characters.")]
        public string RegNo { get; set; } = string.Empty;

        [Required(ErrorMessage = "prompt is required.")]
        public string Prompt { get; set; } = string.Empty;

        public string? SystemPrompt { get; set; }

        public string? ModelOverride { get; set; }

        [Range(1, 8096, ErrorMessage = "maxTokens must be between 1 and 8096.")]
        public int? MaxTokensOverride { get; set; }

        public string? CorrelationId { get; set; }
    }
}