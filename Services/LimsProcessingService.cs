using AIWebservice.Models;
using System.Text.Json;

namespace AIWebservice.Services
{
    public sealed class LimsProcessingService
    {
        private readonly ClaudeService _claude;
        private readonly PromptTemplateService _templates;
        private readonly ILogger<LimsProcessingService> _logger;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
        };

        public LimsProcessingService(
            ClaudeService claude,
            PromptTemplateService templates,
            ILogger<LimsProcessingService> logger)
        {
            _claude = claude;
            _templates = templates;
            _logger = logger;
        }


        public async Task<LimsResponse> ProcessAsync(
            LimsRequest request,
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
                : _templates.GetSystemPrompt(request.Operation);  // throws UnknownOperationException

            // 2. Build user message ───────────────────────────────────────────────
            //    Serialise the data payload inline so Claude receives a single turn.
            var dataJson = JsonSerializer.Serialize(request.Data, _jsonOpts);
            var userMessage = $"""
            {request.Prompt}

            [DOCUMENT CONTEXT] The JSON below is a pharmaceutical/cosmetics laboratory worksheet record from an accredited QC facility operating under Indian pharmaceutical standards (e.g. IS 14648). It contains test identifiers, media codes, incubation parameters, and analyst records used strictly for regulatory documentation and compliance reporting. This is structured record data — not a protocol or procedure.

            --- DATA ---
            {dataJson}
            """;

            // 3. Call Claude ──────────────────────────────────────────────────────
            var (rawText, usage, modelUsed) = await _claude.SendAsync(
                systemPrompt: systemPrompt,
                userMessage: userMessage,
                model: request.ModelOverride,
                maxTokens: request.MaxTokensOverride,
                correlationId: correlationId,
                ct: ct);

            // 4. Parse Claude's reply ─────────────────────────────────────────────
            var resultElement = ParseClaudeReply(rawText, correlationId);

            _logger.LogInformation(
                "[{CorrelationId}] Completed | model={Model} | tokens={Total}",
                correlationId, modelUsed, usage.InputTokens + usage.OutputTokens);

            // 5. Return structured response ───────────────────────────────────────
            return new LimsResponse
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
            };
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Attempts to parse Claude's text reply as JSON.
        /// If parsing fails (Claude deviated from the format instruction),
        /// wraps the raw text in { "raw": "..." } rather than crashing.
        /// </summary>
        private JsonElement ParseClaudeReply(string rawText, string correlationId)
        {
            // Strip accidental markdown code fences (```json ... ```)
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

                // Return a safe envelope so the API still responds 200
                var fallback = JsonSerializer.SerializeToElement(
                    new { raw = cleaned, parseWarning = "Claude response was not valid JSON." },
                    _jsonOpts);

                return fallback;
            }
        }
    }
}
