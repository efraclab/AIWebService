namespace AIWebservice.Configuration
{
    public sealed class AnthropicSettings
    {
        public const string SectionName = "Anthropic";

        public string ApiKey { get; init; } = string.Empty;

        public string DefaultModel { get; init; } = "claude-sonnet-4-6";

        public int MaxTokens { get; init; } = 1024;

        public string BaseUrl { get; init; } = "https://api.anthropic.com";

        public string ApiVersion { get; init; } = "2023-06-01";

        public int TimeoutSeconds { get; init; } = 60;
    }
}
