using System.Text.Json;
using System.Text.Json.Serialization;

namespace AIWebservice.Models
{
    public sealed class LimsResponse
    {
        public string CorrelationId { get; init; } = string.Empty;

        public bool Success { get; init; } = true;

        public string Operation { get; init; } = string.Empty;

        public JsonElement Result { get; init; }

        public TokenUsage Usage { get; init; } = new();

        public string Model { get; init; } = string.Empty;

        public DateTimeOffset ProcessedAt { get; init; } = DateTimeOffset.UtcNow;
    }

    public sealed class TokenUsage
    {
        public int InputTokens { get; init; }
        public int OutputTokens { get; init; }
        public int TotalTokens => InputTokens + OutputTokens;
    }

    public sealed class ErrorResponse
    {
        public string CorrelationId { get; init; } = string.Empty;
        public bool Success { get; init; } = false;
        public string ErrorCode { get; init; } = string.Empty;
        public string Message { get; init; } = string.Empty;

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public IDictionary<string, string[]>? ValidationErrors { get; init; }

        public DateTimeOffset OccurredAt { get; init; } = DateTimeOffset.UtcNow;
    }
}
