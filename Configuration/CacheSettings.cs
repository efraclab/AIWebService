namespace AIWebservice.Configuration
{
    public sealed class CacheSettings
    {
        public const string SectionName = "Cache";

        public int TtlMinutes { get; init; } = 30;
    }
}
