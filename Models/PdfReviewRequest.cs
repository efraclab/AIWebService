using System.ComponentModel.DataAnnotations;

namespace AIWebservice.Models
{
    public sealed class PdfReviewRequest
    {
        [Required(ErrorMessage = "At least one PDF file is required.")]
        public List<IFormFile> Files { get; init; } = new();

        [Required(ErrorMessage = "prompt is required.")]
        //[StringLength(8000, ErrorMessage = "prompt must be ≤ 8000 characters.")]
        public string Prompt { get; init; } = string.Empty;

        //[StringLength(8000, ErrorMessage = "systemPrompt must be ≤ 8000 characters.")]
        public string? SystemPrompt { get; init; }

        public string? ModelOverride { get; init; }

        [Range(1, 8096, ErrorMessage = "maxTokens must be between 1 and 8096.")]
        public int? MaxTokensOverride { get; init; }

        public string? CorrelationId { get; init; }

        /// <summary>
        /// When true, uploaded files are deleted from Anthropic storage after the review
        /// completes. Default is true so that storage does not accumulate unused files.
        /// </summary>
        public bool DeleteFilesAfter { get; init; } = true;
    }
}
