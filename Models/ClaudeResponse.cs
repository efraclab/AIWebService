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
        [property: JsonPropertyName("output_tokens")] int OutputTokens
    );

    public sealed record ClaudeApiError(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("error")] ClaudeErrorDetail Error
    );

    public sealed record ClaudeErrorDetail(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("message")] string Message
    );
}
