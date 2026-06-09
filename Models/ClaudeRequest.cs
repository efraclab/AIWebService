using System.Text.Json.Serialization;

namespace AIWebservice.Models
{
    public sealed record ClaudeApiRequest(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] IReadOnlyList<ClaudeSystemBlock> System,
        [property: JsonPropertyName("messages")] IReadOnlyList<ClaudeMessage> Messages
    );

    public sealed record ClaudeSystemBlock(
        [property: JsonPropertyName("type")] string Type,
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("cache_control")] CacheControl? CacheControl = null
    );

    public sealed record CacheControl(
        [property: JsonPropertyName("type")] string Type
    );

    public sealed record ClaudeMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] string Content
    );

    // ── Block-based message variant (used when content includes documents/files) ──

    public sealed record ClaudeApiRequestWithBlocks(
        [property: JsonPropertyName("model")] string Model,
        [property: JsonPropertyName("max_tokens")] int MaxTokens,
        [property: JsonPropertyName("system")] IReadOnlyList<ClaudeSystemBlock> System,
        [property: JsonPropertyName("messages")] IReadOnlyList<ClaudeBlockMessage> Messages
    );

    public sealed record ClaudeBlockMessage(
        [property: JsonPropertyName("role")] string Role,
        [property: JsonPropertyName("content")] IReadOnlyList<object> Content
    );

    public sealed record ClaudeDocumentBlock(
        [property: JsonPropertyName("source")] ClaudeDocumentSource Source,
        [property: JsonPropertyName("type")] string Type = "document"
    );

    public sealed record ClaudeDocumentSource(
        [property: JsonPropertyName("file_id")] string FileId,
        [property: JsonPropertyName("type")] string Type = "file"
    );

    public sealed record ClaudeTextBlock(
        [property: JsonPropertyName("text")] string Text,
        [property: JsonPropertyName("type")] string Type = "text"
    );
}
