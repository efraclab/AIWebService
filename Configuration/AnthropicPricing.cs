namespace AIWebservice.Configuration
{
    public static class AnthropicPricing
    {
        // Update these values whenever Anthropic changes pricing.

        // USD per 1,000,000 tokens
        public const decimal SonnetInput = 3.00m;
        public const decimal SonnetOutput = 15.00m;

        // Prompt caching
        public const decimal CacheWrite = 3.75m;
        public const decimal CacheRead = 0.30m;

        public static decimal Calculate(
            int inputTokens,
            int outputTokens,
            int cacheWriteTokens,
            int cacheReadTokens)
        {
            return Math.Round(
                (
                    inputTokens * SonnetInput +
                    outputTokens * SonnetOutput +
                    cacheWriteTokens * CacheWrite +
                    cacheReadTokens * CacheRead
                ) / 1_000_000m,
                6);
        }
    }
}