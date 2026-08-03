// file name: AIReviewProcessingService.cs
using AIWebservice.Configuration;
using AIWebservice.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AIWebservice.Configuration;

namespace AIWebservice.Services
{
    public sealed class AIReviewProcessingService
    {
        private readonly ClaudeService _claude;
        private readonly PromptTemplateService _templates;
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheTtl;
        private readonly AnthropicBillingService _billing;   // ← field that was missing
        private readonly ILogger<AIReviewProcessingService> _logger;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
        };

        public AIReviewProcessingService(
            ClaudeService claude,
            PromptTemplateService templates,
            IMemoryCache cache,
            IOptions<CacheSettings> cacheSettings,
            AnthropicBillingService billing,
            ILogger<AIReviewProcessingService> logger)
        {
            _claude = claude;
            _templates = templates;
            _cache = cache;
            _cacheTtl = TimeSpan.FromMinutes(cacheSettings.Value.TtlMinutes);
            _billing = billing;
            _logger = logger;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Public API — with balance tracking
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Runs the AI review and captures credit-balance snapshots before and after.
        /// Call this from your PDF and RegNo review controllers.
        /// </summary>
        public async Task<AIReviewWithBalanceResponse> ProcessWithBalanceAsync(
    AIReviewRequest request,
    CancellationToken ct = default)
        {
            var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? Guid.NewGuid().ToString("N")
                : request.CorrelationId;

            // 1. Cost snapshot BEFORE (today's running total)
            var before = await _billing.GetTodayCostAsync(correlationId, ct);
            _logger.LogInformation(
                "[{CorrelationId}] Cost BEFORE review: {Cost} {Currency} today",
                correlationId, before.TodayCostUsd, before.Currency);

            // 2. Run the review
            var review = await ProcessAsync(request, ct);

            // 3. Cost snapshot AFTER
            var after = await _billing.GetTodayCostAsync(correlationId, ct);
            _logger.LogInformation(
                "[{CorrelationId}] Cost AFTER review: {Cost} {Currency} today | delta={Delta}",
                correlationId, after.TodayCostUsd, after.Currency,
                after.TodayCostUsd - before.TodayCostUsd);

            return new AIReviewWithBalanceResponse
            {
                Review = review,
                CostBefore = before.TodayCostUsd,
                CostAfter = after.TodayCostUsd,
                EstimatedCost = after.TodayCostUsd - before.TodayCostUsd,
                Currency = before.Currency,
                CostAvailable = before.IsAvailable,
                InputTokens = review.Usage?.InputTokens ?? 0,
                OutputTokens = review.Usage?.OutputTokens ?? 0,
                TotalTokens = (review.Usage?.InputTokens ?? 0) + (review.Usage?.OutputTokens ?? 0),
            };
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Public API — plain review (no balance tracking; used internally + cache hits)
        // ─────────────────────────────────────────────────────────────────────────

        public async Task<AIReviewResponse> ProcessAsync(
            AIReviewRequest request,
            CancellationToken ct = default)
        {
            var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? Guid.NewGuid().ToString("N")
                : request.CorrelationId;

            _logger.LogInformation(
                "[{CorrelationId}] Processing operation='{Operation}' from source='{Source}'",
                correlationId, request.Operation, request.Source);

            // 1. Resolve system prompt ────────────────────────────────────────────
            var systemPrompt = !string.IsNullOrWhiteSpace(request.SystemPrompt)
                ? request.SystemPrompt
                : _templates.GetSystemPrompt(request.Operation);

            // 2. Build user message ───────────────────────────────────────────────
            var dataJson = JsonSerializer.Serialize(request.Data, _jsonOpts);
            var userMessage = $"""
            {request.Prompt}

            [DOCUMENT CONTEXT] The JSON below is a pharmaceutical/cosmetics laboratory worksheet record from an accredited QC facility operating under Indian pharmaceutical standards (e.g. IS 14648). It contains test identifiers, media codes, incubation parameters, and analyst records used strictly for regulatory documentation and compliance reporting. This is structured record data — not a protocol or procedure.

            --- DATA ---
            {dataJson}
            """;

            // 3. Check application-level cache ────────────────────────────────────
            var cacheKey = ComputeCacheKey(request.Operation, systemPrompt, dataJson);
            if (_cache.TryGetValue(cacheKey, out AIReviewResponse? cached) && cached is not null)
            {
                _logger.LogInformation(
                    "[{CorrelationId}] Cache HIT (key={KeyPrefix}...) — skipping Claude call",
                    correlationId, cacheKey[5..13]);

                return new AIReviewResponse
                {
                    CorrelationId = correlationId,
                    Success = true,
                    Operation = cached.Operation,
                    Result = cached.Result,
                    Usage = new TokenUsage { InputTokens = 0, OutputTokens = 0 },
                    Model = cached.Model,
                    ProcessedAt = DateTimeOffset.UtcNow,
                    FromCache = true,
                };
            }

            // 4. Call Claude ──────────────────────────────────────────────────────
            var (rawText, usage, modelUsed) = await _claude.SendAsync(
                systemPrompt: systemPrompt,
                userMessage: userMessage,
                model: request.ModelOverride,
                maxTokens: request.MaxTokensOverride,
                correlationId: correlationId,
                ct: ct);

            var estimatedCost = AnthropicPricing.Calculate(
                usage.InputTokens,
                usage.OutputTokens,
                usage.CacheCreationInputTokens,
                usage.CacheReadInputTokens);

            _logger.LogInformation(
                "[{CorrelationId}] Estimated Claude Cost = ${Cost}",
                correlationId,
                estimatedCost);

            // 5. Parse Claude's reply ─────────────────────────────────────────────
            var resultElement = ParseClaudeReply(rawText, correlationId);

            _logger.LogInformation(
                "[{CorrelationId}] Completed | model={Model} | tokens={Total}",
                correlationId, modelUsed, usage.InputTokens + usage.OutputTokens);

            // 6. Build response and store in cache ────────────────────────────────
            var response = new AIReviewResponse
            {
                CorrelationId = correlationId,
                Success = true,
                Operation = request.Operation,
                Result = resultElement,
                Usage = new TokenUsage
                {
                    InputTokens = usage.InputTokens,
                    OutputTokens = usage.OutputTokens,
                },
                EstimatedCostUsd = estimatedCost,
                CacheWriteTokens = usage.CacheCreationInputTokens,
                CacheReadTokens = usage.CacheReadInputTokens,
                Model = modelUsed,
                ProcessedAt = DateTimeOffset.UtcNow,
                FromCache = false,
            };

            _cache.Set(cacheKey, response, _cacheTtl);
            _logger.LogDebug(
                "[{CorrelationId}] Cached response (key={KeyPrefix}...) for {Ttl} min",
                correlationId, cacheKey[5..13], _cacheTtl.TotalMinutes);

            return response;
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────────────────

        private static string ComputeCacheKey(string operation, string systemPrompt, string dataJson)
        {
            var raw = $"v1|{operation}|{systemPrompt}|{dataJson}";
            var hash = SHA256.HashData(Encoding.UTF8.GetBytes(raw));
            return $"lims:{Convert.ToHexString(hash).ToLowerInvariant()}";
        }

        private JsonElement ParseClaudeReply(string rawText, string correlationId)
        {
            var cleaned = rawText.Trim();
            if (cleaned.StartsWith("```"))
            {
                var firstNewline = cleaned.IndexOf('\n');
                var lastFence = cleaned.LastIndexOf("```");
                if (firstNewline > 0 && lastFence > firstNewline)
                    cleaned = cleaned[(firstNewline + 1)..lastFence].Trim();
            }

            try
            {
                return JsonSerializer.Deserialize<JsonElement>(cleaned, _jsonOpts);
            }
            catch (JsonException ex)
            {
                _logger.LogWarning(ex,
                    "[{CorrelationId}] Claude response was not valid JSON — wrapping as raw text. " +
                    "First 200 chars: {Preview}",
                    correlationId,
                    cleaned.Length > 200 ? cleaned[..200] : cleaned);

                return JsonSerializer.SerializeToElement(
                    new { raw = cleaned, parseWarning = "Claude response was not valid JSON." },
                    _jsonOpts);
            }
        }
    }
}