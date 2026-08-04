namespace AIWebservice.Configuration
{
    public sealed class AnthropicSettings
    {
        public const string SectionName = "Anthropic";

        public string ApiKey { get; init; } = string.Empty;
        public string AdminApiKey    { get; init; } = string.Empty;

        // ⚠ Verify this is a real, currently-supported model ID via the Console or
        // GET /v1/models before deploying — do not assume this string resolves as expected.
        public string DefaultModel { get; init; } = "claude-sonnet-4-6";

        // ⚠ Verify this against the real model's max output token limit (check the
        // Console / model card for the model above) — this value and the
        // [Range(1, 8096)] validator on AIReviewRequest.MaxTokensOverride currently
        // disagree with each other and should be reconciled.
        public int MaxTokens { get; init; } = 100000;

        // Low/zero temperature for deterministic rule-checking tasks (CoA/QC review).
        // Reduces (but does not eliminate) run-to-run variance in which findings surface.
        public double DefaultTemperature { get; init; } = 0.0;

        public string BaseUrl { get; init; } = "https://api.anthropic.com";

        public string ApiVersion { get; init; } = "2023-06-01";

        public int TimeoutSeconds { get; init; } = 60;
    }
}