using AIWebservice.Configuration;
using AIWebservice.Models;
using Microsoft.Extensions.Options;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace AIWebservice.Services
{
    public sealed class ClaudeService
    {
        private readonly HttpClient _http;
        private readonly AnthropicSettings _settings;
        private readonly ILogger<ClaudeService> _logger;

        private static readonly JsonSerializerOptions _jsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,
            PropertyNameCaseInsensitive = true,
        };

        public ClaudeService(
            HttpClient http,
            IOptions<AnthropicSettings> settings,
            ILogger<ClaudeService> logger)
        {
            _http = http;
            _settings = settings.Value;
            _logger = logger;

            // Configure shared headers on the HttpClient (registered as typed client).
            _http.BaseAddress = new Uri(_settings.BaseUrl);
            _http.Timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
            _http.DefaultRequestHeaders.Add("x-api-key", _settings.ApiKey);
            _http.DefaultRequestHeaders.Add("anthropic-version", _settings.ApiVersion);
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Public API
        // ─────────────────────────────────────────────────────────────────────────

        /// <summary>
        /// Sends a system + user prompt pair to Claude and returns the raw text reply.
        /// </summary>
        /// <param name="systemPrompt">The system turn that sets Claude's role/output contract.</param>
        /// <param name="userMessage">The user turn containing the prompt and serialised data.</param>
        /// <param name="model">Claude model string; falls back to configured default.</param>
        /// <param name="maxTokens">Token budget; falls back to configured default.</param>
        /// <param name="correlationId">For structured log correlation.</param>
        /// <param name="ct">Cancellation token.</param>
        private const int MaxRetries = 2;

        public async Task<(string Text, ClaudeUsage Usage, string Model)> SendAsync(
            string systemPrompt,
            string userMessage,
            string? model = null,
            int? maxTokens = null,
            string? correlationId = null,
            CancellationToken ct = default)
        {
            var effectiveModel = string.IsNullOrWhiteSpace(model) ? _settings.DefaultModel : model;
            var effectiveMaxTokens = maxTokens ?? _settings.MaxTokens;

            var requestBody = new ClaudeApiRequest(
                Model: effectiveModel,
                MaxTokens: effectiveMaxTokens,
                System: systemPrompt,
                Messages: [new ClaudeMessage("user", userMessage)]
            );

            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);

            _logger.LogDebug(
                "[{CorrelationId}] → Claude {Model} | max_tokens={MaxTokens} | sys={SysLen} chars | user={UserLen} chars",
                correlationId, effectiveModel, effectiveMaxTokens,
                systemPrompt.Length, userMessage.Length);

            for (var attempt = 0; attempt <= MaxRetries; attempt++)
            {
                HttpResponseMessage response;
                var requestContent = new StringContent(json, Encoding.UTF8, "application/json");

                try
                {
                    response = await _http.PostAsync("/v1/messages", requestContent, ct);
                }
                catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
                {
                    _logger.LogError("[{CorrelationId}] Claude HTTP call timed out after {Timeout}s",
                        correlationId, _settings.TimeoutSeconds);

                    throw new AnthropicTimeoutException(
                        $"The request to the Anthropic API timed out after {_settings.TimeoutSeconds} seconds.", ex);
                }
                catch (HttpRequestException ex)
                {
                    _logger.LogError(ex, "[{CorrelationId}] Network error reaching Anthropic API", correlationId);
                    throw new AnthropicApiException(0, "network_error",
                        $"A network error occurred while contacting the Anthropic API: {ex.Message}");
                }

                await EnsureSuccessAsync(response, correlationId, ct);

                var responseJson = await response.Content.ReadAsStringAsync(ct);

                ClaudeApiResponse? parsed;
                try
                {
                    parsed = JsonSerializer.Deserialize<ClaudeApiResponse>(responseJson, _jsonOptions);
                }
                catch (JsonException ex)
                {
                    _logger.LogError(ex,
                        "[{CorrelationId}] Failed to deserialise Anthropic response body", correlationId);
                    throw new AnthropicApiException(500, "parse_error",
                        "Anthropic returned a response that could not be parsed.");
                }

                if (parsed is not null && parsed.Content.Count > 0)
                {
                    var text = string.Concat(parsed.Content.Select(b => b.Text));
                    var usage = parsed.Usage;

                    _logger.LogInformation(
                        "[{CorrelationId}] ← Claude {Model} | in={In} out={Out} tokens | stop={Stop}",
                        correlationId, parsed.Model,
                        usage.InputTokens, usage.OutputTokens, parsed.StopReason);

                    return (text, usage, parsed.Model);
                }

                var stopReason = parsed?.StopReason ?? "null_response";

                // Refusals are deterministic — retrying won't help.
                if (stopReason == "refusal")
                {
                    _logger.LogError(
                        "[{CorrelationId}] Claude refused the request (stop_reason=refusal). Raw={Raw}",
                        correlationId, responseJson.Length > 500 ? responseJson[..500] : responseJson);
                    throw new AnthropicApiException(500, "content_refusal",
                        "The AI model refused to process this request due to content policy. " +
                        "Ensure the system prompt establishes a clear compliance/data-auditing context.");
                }

                if (attempt < MaxRetries)
                {
                    var delayMs = 500 * (attempt + 1);
                    _logger.LogWarning(
                        "[{CorrelationId}] Empty content array (stop_reason={StopReason}), retrying attempt {Attempt}/{MaxRetries} after {Delay}ms. Raw={Raw}",
                        correlationId, stopReason, attempt + 1, MaxRetries, responseJson.Length > 500 ? responseJson[..500] : responseJson);
                    await Task.Delay(delayMs, ct);
                }
                else
                {
                    _logger.LogError(
                        "[{CorrelationId}] Empty content array after {Attempts} attempts (stop_reason={StopReason}). Raw={Raw}",
                        correlationId, MaxRetries + 1, stopReason, responseJson.Length > 500 ? responseJson[..500] : responseJson);
                }
            }

            throw new AnthropicApiException(500, "empty_response",
                $"Anthropic returned an empty content array after {MaxRetries + 1} attempts.");
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────────────────

        private async Task EnsureSuccessAsync(
            HttpResponseMessage response,
            string? correlationId,
            CancellationToken ct)
        {
            if (response.IsSuccessStatusCode) return;

            var statusCode = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(ct);

            _logger.LogError(
                "[{CorrelationId}] Anthropic returned HTTP {StatusCode}: {Body}",
                correlationId, statusCode, body);

            // Try to parse the Anthropic error envelope
            ClaudeApiError? errorEnvelope = null;
            try { errorEnvelope = JsonSerializer.Deserialize<ClaudeApiError>(body, _jsonOptions); }
            catch { /* best-effort; fall through */ }

            var errorType = errorEnvelope?.Error?.Type ?? "unknown_error";
            var errorMessage = errorEnvelope?.Error?.Message ?? body;

            // Map to domain exceptions
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    throw new AnthropicAuthException(
                        $"Anthropic authentication failed ({statusCode}): {errorMessage}");

                case HttpStatusCode.TooManyRequests:
                case (HttpStatusCode)529:   // Anthropic overloaded
                    throw new AnthropicRateLimitException(statusCode,
                        $"Anthropic rate limit / overload ({statusCode}): {errorMessage}");

                default:
                    throw new AnthropicApiException(statusCode, errorType,
                        $"Anthropic API error ({statusCode}) [{errorType}]: {errorMessage}");
            }
        }
    }
}
