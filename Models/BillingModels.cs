
// file name: BillingModels.cs

namespace AIWebservice.Models
{
    /// <summary>
    /// Cost snapshot returned by AnthropicBillingService.
    /// NOTE: Anthropic has no credit-balance API endpoint.
    /// TodayCostUsd = actual USD spent today per /v1/organizations/cost_report.
    /// Data has up to ~5 min delay after usage.
    /// </summary>
    public sealed class UsageCostSnapshot
    {
        /// <summary>Total USD spent in the requested period (today by default).</summary>
        public decimal TodayCostUsd { get; init; }

        public string Currency    { get; init; } = "USD";
        public string PeriodStart { get; init; } = string.Empty;
        public string PeriodEnd   { get; init; } = string.Empty;

        /// <summary>
        /// False when AdminApiKey is missing or account is not an Organisation.
        /// All values will be 0 / empty — the review flow is unaffected.
        /// </summary>
        public bool IsAvailable { get; init; }

        public static readonly UsageCostSnapshot Unavailable = new()
        {
            TodayCostUsd = 0m,
            Currency     = "USD",
            IsAvailable  = false,
        };
    }

    /// <summary>
    /// Wraps an AIReviewResponse with cost data taken before and after the Claude call.
    /// costBefore and costAfter are today's running totals — the difference is the
    /// approximate cost of this single review (note: ~5 min delay on Anthropic's side).
    /// </summary>
    public sealed class AIReviewWithBalanceResponse
    {
        public AIReviewResponse    Review          { get; init; } = null!;
        public decimal             CostBefore      { get; init; }
        public decimal             CostAfter       { get; init; }
        public decimal             EstimatedCost   { get; init; }  // CostAfter - CostBefore
        public string              Currency        { get; init; } = "USD";
        public bool                CostAvailable   { get; init; }

        // Token-level cost is always accurate (comes directly from Claude response)
        public int InputTokens  { get; init; }
        public int OutputTokens { get; init; }
        public int TotalTokens  { get; init; }
    }
}