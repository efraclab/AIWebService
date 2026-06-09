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

        private const string BetaPromptCaching = "prompt-caching-2024-07-31";
        private const string BetaFilesApi = "files-api-2025-04-14";

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
            // NOTE: anthropic-beta is set per-request so different endpoints can opt into
            // different beta features (e.g. prompt-caching vs. files-api).
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

        private const int MaxRetries = 2;

        /// <summary>
        /// Sends a system + user prompt pair to Claude and returns the raw text reply.
        /// </summary>
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
                System: [new ClaudeSystemBlock("text", systemPrompt, new CacheControl("ephemeral"))],
                Messages: [new ClaudeMessage("user", userMessage)]
            );

            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);

            _logger.LogDebug(
                "[{CorrelationId}] → Claude {Model} | max_tokens={MaxTokens} | sys={SysLen} chars | user={UserLen} chars",
                correlationId, effectiveModel, effectiveMaxTokens,
                systemPrompt.Length, userMessage.Length);

            return await PostMessagesAsync(json, [BetaPromptCaching], correlationId, ct);
        }

        /// <summary>
        /// Sends a messages request that references one or more uploaded files via file_id
        /// (Anthropic Files API beta). PDFs are referenced as document content blocks; no
        /// base64 encoding is required.
        /// </summary>
        public async Task<(string Text, ClaudeUsage Usage, string Model)> SendWithDocumentsAsync(
            string systemPrompt,
            string userPrompt,
            IReadOnlyList<string> fileIds,
            string? model = null,
            int? maxTokens = null,
            string? correlationId = null,
            CancellationToken ct = default)
        {
            if (fileIds is null || fileIds.Count == 0)
                throw new ArgumentException("At least one file id must be supplied.", nameof(fileIds));

            var effectiveModel = string.IsNullOrWhiteSpace(model) ? _settings.DefaultModel : model;
            var effectiveMaxTokens = maxTokens ?? _settings.MaxTokens;

            // Order: documents first, then the textual prompt — Anthropic recommends this for
            // best citation/answer quality.
            var contentBlocks = new List<object>(fileIds.Count + 1);
            foreach (var fid in fileIds)
                contentBlocks.Add(new ClaudeDocumentBlock(new ClaudeDocumentSource(fid)));
            contentBlocks.Add(new ClaudeTextBlock(userPrompt));

            var requestBody = new ClaudeApiRequestWithBlocks(
                Model: effectiveModel,
                MaxTokens: effectiveMaxTokens,
                System: [new ClaudeSystemBlock("text", systemPrompt, new CacheControl("ephemeral"))],
                Messages: [new ClaudeBlockMessage("user", contentBlocks)]
            );

            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);

            _logger.LogDebug(
                "[{CorrelationId}] → Claude {Model} | max_tokens={MaxTokens} | files={FileCount} | prompt={PromptLen} chars",
                correlationId, effectiveModel, effectiveMaxTokens,
                fileIds.Count, userPrompt.Length);

            return await PostMessagesAsync(json, [BetaPromptCaching, BetaFilesApi], correlationId, ct);
        }

        /// <summary>
        /// Uploads a file to the Anthropic Files API and returns its metadata (including the
        /// file_id used to reference the file in subsequent messages calls).
        /// </summary>
        public async Task<ClaudeFileUploadResponse> UploadFileAsync(
            Stream content,
            string fileName,
            string mimeType,
            string? correlationId = null,
            CancellationToken ct = default)
        {
            using var form = new MultipartFormDataContent();
            var fileContent = new StreamContent(content);
            fileContent.Headers.ContentType = new MediaTypeHeaderValue(mimeType);
            form.Add(fileContent, "file", fileName);

            using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/files")
            {
                Content = form,
            };
            request.Headers.Add("anthropic-beta", BetaFilesApi);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                _logger.LogError("[{CorrelationId}] Anthropic file upload timed out after {Timeout}s",
                    correlationId, _settings.TimeoutSeconds);
                throw new AnthropicTimeoutException(
                    $"The file upload to the Anthropic API timed out after {_settings.TimeoutSeconds} seconds.", ex);
            }
            catch (HttpRequestException ex)
            {
                _logger.LogError(ex, "[{CorrelationId}] Network error uploading file to Anthropic API", correlationId);
                throw new AnthropicApiException(0, "network_error",
                    $"A network error occurred while uploading the file: {ex.Message}");
            }

            await EnsureSuccessAsync(response, correlationId, ct);

            var responseJson = await response.Content.ReadAsStringAsync(ct);

            ClaudeFileUploadResponse? parsed;
            try
            {
                parsed = JsonSerializer.Deserialize<ClaudeFileUploadResponse>(responseJson, _jsonOptions);
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "[{CorrelationId}] Failed to deserialise Anthropic file upload response", correlationId);
                throw new AnthropicApiException(500, "parse_error",
                    "Anthropic returned a file upload response that could not be parsed.");
            }

            if (parsed is null || string.IsNullOrWhiteSpace(parsed.Id))
                throw new AnthropicApiException(500, "invalid_response",
                    "Anthropic file upload response did not contain an id.");

            _logger.LogInformation(
                "[{CorrelationId}] Uploaded file {FileName} ({SizeBytes} bytes, {MimeType}) → {FileId}",
                correlationId, parsed.Filename, parsed.SizeBytes, parsed.MimeType, parsed.Id);

            return parsed;
        }

        /// <summary>
        /// Best-effort delete of an uploaded file. Failures are logged but not thrown so that
        /// cleanup never breaks the caller's primary flow.
        /// </summary>
        public async Task DeleteFileAsync(
            string fileId,
            string? correlationId = null,
            CancellationToken ct = default)
        {
            try
            {
                using var request = new HttpRequestMessage(HttpMethod.Delete, $"/v1/files/{fileId}");
                request.Headers.Add("anthropic-beta", BetaFilesApi);

                using var response = await _http.SendAsync(request, ct);
                if (!response.IsSuccessStatusCode)
                {
                    var body = await response.Content.ReadAsStringAsync(ct);
                    _logger.LogWarning(
                        "[{CorrelationId}] Failed to delete uploaded file {FileId}: HTTP {Status} {Body}",
                        correlationId, fileId, (int)response.StatusCode, body);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex,
                    "[{CorrelationId}] Exception while deleting uploaded file {FileId}",
                    correlationId, fileId);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────────────────

        private async Task<(string Text, ClaudeUsage Usage, string Model)> PostMessagesAsync(
            string json,
            IReadOnlyList<string> betaFeatures,
            string? correlationId,
            CancellationToken ct)
        {
            for (var attempt = 0; attempt <= MaxRetries; attempt++)
            {
                using var request = new HttpRequestMessage(HttpMethod.Post, "/v1/messages")
                {
                    Content = new StringContent(json, Encoding.UTF8, "application/json"),
                };
                if (betaFeatures.Count > 0)
                    request.Headers.Add("anthropic-beta", string.Join(",", betaFeatures));

                HttpResponseMessage response;
                try
                {
                    response = await _http.SendAsync(request, ct);
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
                        "[{CorrelationId}] ← Claude {Model} | in={In} out={Out} tokens | cache_write={CW} cache_read={CR} | stop={Stop}",
                        correlationId, parsed.Model,
                        usage.InputTokens, usage.OutputTokens,
                        usage.CacheCreationInputTokens, usage.CacheReadInputTokens,
                        parsed.StopReason);

                    return (text, usage, parsed.Model);
                }

                var stopReason = parsed?.StopReason ?? "null_response";

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
                        correlationId, stopReason, attempt + 1, MaxRetries, delayMs, responseJson.Length > 500 ? responseJson[..500] : responseJson);
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

            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    throw new AnthropicAuthException(
                        $"Anthropic authentication failed ({statusCode}): {errorMessage}");

                case HttpStatusCode.TooManyRequests:
                case (HttpStatusCode)529:
                    throw new AnthropicRateLimitException(statusCode,
                        $"Anthropic rate limit / overload ({statusCode}): {errorMessage}");

                default:
                    throw new AnthropicApiException(statusCode, errorType,
                        $"Anthropic API error ({statusCode}) [{errorType}]: {errorMessage}");
            }
        }
    }
}
