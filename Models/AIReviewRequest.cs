using System.ComponentModel.DataAnnotations;
using System.Text.Json;

namespace AIWebservice.Models
{
    public sealed class AIReviewRequest
    {
        [Required(ErrorMessage = "source is required.")]
        [StringLength(100, ErrorMessage = "source must be ≤ 100 characters.")]
        public string Source { get; init; } = string.Empty;

        [Required(ErrorMessage = "operation is required.")]
        [StringLength(100, ErrorMessage = "operation must be ≤ 100 characters.")]
        public string Operation { get; init; } = string.Empty;

        [StringLength(8000, ErrorMessage = "systemPrompt must be ≤ 8000 characters.")]
        public string? SystemPrompt { get; init; }

        [Required(ErrorMessage = "prompt is required.")]
        [StringLength(4000, ErrorMessage = "prompt must be ≤ 4000 characters.")]
        public string Prompt { get; init; } = string.Empty;

        [Required(ErrorMessage = "data is required.")]
        public JsonElement Data { get; init; }

        public string? ModelOverride { get; init; }

        [Range(1, 8096, ErrorMessage = "maxTokens must be between 1 and 8096.")]
        public int? MaxTokensOverride { get; init; }

        public string? CorrelationId { get; init; }
    }
}
