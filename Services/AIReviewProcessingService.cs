using AIWebservice.Configuration;
using AIWebservice.Models;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace AIWebservice.Services
{
    public sealed class AIReviewProcessingService
    {
        private readonly ClaudeService _claude;
        private readonly PromptTemplateService _templates;
        private readonly IMemoryCache _cache;
        private readonly TimeSpan _cacheTtl;
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
            ILogger<AIReviewProcessingService> logger)
        {
            _claude = claude;
            _templates = templates;
            _cache = cache;
            _cacheTtl = TimeSpan.FromMinutes(cacheSettings.Value.TtlMinutes);
            _logger = logger;
        }

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
                    "[{CorrelationId}] Claude response was not valid JSON - wrapping as raw text. " +
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
