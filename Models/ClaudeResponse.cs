using System.Text.Json.Serialization;

namespace AIWebservice.Models
{
    public sealed record ClaudeApiResponse(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("type")] string Type,
    [property: JsonPropertyName("role")] string Role,
    [property: JsonPropertyName("content")] IReadOnlyList<ClaudeContentBlock> Content,
    [property: JsonPropertyName("model")] string Model,
    [property: JsonPropertyName("stop_reason")] string? StopReason,
    [property: JsonPropertyName("usage")] ClaudeUsage Usage
);

    public sealed record ClaudeContentBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text
    );

    public sealed record ClaudeUsage(
        [property: JsonPropertyName("input_tokens")] int InputTokens,
<<<<<<< HEAD
        [property: JsonPropertyName("output_tokens")] int OutputTokens
=======
        [property: JsonPropertyName("output_tokens")] int OutputTokens,
        [property: JsonPropertyName("cache_creation_input_tokens")] int CacheCreationInputTokens = 0,
        [property: JsonPropertyName("cache_read_input_tokens")] int CacheReadInputTokens = 0
>>>>>>> origin/main
    );

    public sealed record ClaudeApiError(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("error")] ClaudeErrorDetail Error
    );

    public sealed record ClaudeErrorDetail(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("message")] string Message
    );
<<<<<<< HEAD
=======

    public sealed record ClaudeFileUploadResponse(
        [property: JsonPropertyName("id")] string Id,
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("filename")] string Filename,
        [property: JsonPropertyName("mime_type")] string MimeType,
        [property: JsonPropertyName("size_bytes")] long SizeBytes,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt
    );
>>>>>>> origin/main
}
