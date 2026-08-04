<<<<<<< HEAD
﻿using AIWebservice.Configuration;
=======
using AIWebservice.Configuration;
>>>>>>> origin/main
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

<<<<<<< HEAD
=======
        private const string BetaPromptCaching = "prompt-caching-2024-07-31";
        private const string BetaFilesApi = "files-api-2025-04-14";

>>>>>>> origin/main
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
<<<<<<< HEAD
=======
            // NOTE: anthropic-beta is set per-request so different endpoints can opt into
            // different beta features (e.g. prompt-caching vs. files-api).
>>>>>>> origin/main
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

<<<<<<< HEAD
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

=======
        private const int MaxRetries = 2;

        // Deterministic JSON-extraction tasks (rule-based CoA/report review) don't need
        // visible reasoning and shouldn't burn max_tokens on it. Explicitly disabled by
        // default rather than inheriting whatever the resolved model defaults to.
        // NOTE: some thinking-capable models only accept "disabled" at effort "high" or
        // below — if your resolved model defaults to a higher effort tier and this causes
        // a 400, you'll need to also send an explicit lower "effort" value alongside this.
        private static readonly ClaudeThinkingConfig DisabledThinking = new("disabled");

        /// <summary>
        /// Sends a system + user prompt pair to Claude and returns the raw text reply.
        /// </summary>
>>>>>>> origin/main
        public async Task<(string Text, ClaudeUsage Usage, string Model)> SendAsync(
            string systemPrompt,
            string userMessage,
            string? model = null,
            int? maxTokens = null,
<<<<<<< HEAD
=======
            double? temperature = null,
            bool disableThinking = true,
>>>>>>> origin/main
            string? correlationId = null,
            CancellationToken ct = default)
        {
            var effectiveModel = string.IsNullOrWhiteSpace(model) ? _settings.DefaultModel : model;
            var effectiveMaxTokens = maxTokens ?? _settings.MaxTokens;
<<<<<<< HEAD
=======
            var effectiveTemperature = temperature ?? _settings.DefaultTemperature;
>>>>>>> origin/main

            var requestBody = new ClaudeApiRequest(
                Model: effectiveModel,
                MaxTokens: effectiveMaxTokens,
<<<<<<< HEAD
                System: systemPrompt,
                Messages: [new ClaudeMessage("user", userMessage)]
=======
                System: [new ClaudeSystemBlock("text", systemPrompt, new CacheControl("ephemeral"))],
                Messages: [new ClaudeMessage("user", userMessage)],
                Temperature: effectiveTemperature,
                Thinking: disableThinking ? DisabledThinking : null
>>>>>>> origin/main
            );

            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);

            _logger.LogDebug(
<<<<<<< HEAD
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
=======
                "[{CorrelationId}] → Claude {Model} | max_tokens={MaxTokens} | temperature={Temperature} | thinking_disabled={ThinkingDisabled} | sys={SysLen} chars | user={UserLen} chars",
                correlationId, effectiveModel, effectiveMaxTokens, effectiveTemperature, disableThinking,
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
            double? temperature = null,
            bool disableThinking = true,
            string? correlationId = null,
            CancellationToken ct = default)
        {
            if (fileIds is null || fileIds.Count == 0)
                throw new ArgumentException("At least one file id must be supplied.", nameof(fileIds));

            var effectiveModel = string.IsNullOrWhiteSpace(model) ? _settings.DefaultModel : model;
            var effectiveMaxTokens = maxTokens ?? _settings.MaxTokens;
            var effectiveTemperature = temperature ?? _settings.DefaultTemperature;

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
                Messages: [new ClaudeBlockMessage("user", contentBlocks)],
                Temperature: effectiveTemperature,
                Thinking: disableThinking ? DisabledThinking : null
            );

            var json = JsonSerializer.Serialize(requestBody, _jsonOptions);

            _logger.LogDebug(
                "[{CorrelationId}] → Claude {Model} | max_tokens={MaxTokens} | temperature={Temperature} | thinking_disabled={ThinkingDisabled} | files={FileCount} | prompt={PromptLen} chars",
                correlationId, effectiveModel, effectiveMaxTokens, effectiveTemperature, disableThinking,
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
>>>>>>> origin/main
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
<<<<<<< HEAD
                    var text = string.Concat(parsed.Content.Select(b => b.Text));
                    var usage = parsed.Usage;

                    _logger.LogInformation(
                        "[{CorrelationId}] ← Claude {Model} | in={In} out={Out} tokens | stop={Stop}",
                        correlationId, parsed.Model,
                        usage.InputTokens, usage.OutputTokens, parsed.StopReason);
=======
                    // Only concatenate genuine "text" blocks. Other block types (e.g. "thinking",
                    // "redacted_thinking", future types) must never be treated as the answer —
                    // concatenating them blindly is what let raw reasoning leak into responses.
                    var textBlocks = parsed.Content.Where(b => b.Type == "text").ToList();
                    var text = string.Concat(textBlocks.Select(b => b.Text));
                    var usage = parsed.Usage;

                    var nonTextTypes = parsed.Content
                        .Where(b => b.Type != "text")
                        .Select(b => b.Type)
                        .Distinct()
                        .ToList();

                    _logger.LogInformation(
                        "[{CorrelationId}] ← Claude {Model} | in={In} out={Out} tokens | cache_write={CW} cache_read={CR} | stop={Stop} | blockTypes={BlockTypes}",
                        correlationId, parsed.Model,
                        usage.InputTokens, usage.OutputTokens,
                        usage.CacheCreationInputTokens, usage.CacheReadInputTokens,
                        parsed.StopReason,
                        string.Join(",", parsed.Content.Select(b => b.Type).Distinct()));

                    if (nonTextTypes.Count > 0)
                    {
                        _logger.LogWarning(
                            "[{CorrelationId}] Response contained non-text block types: {Types}",
                            correlationId, string.Join(",", nonTextTypes));
                    }

                    if (string.IsNullOrWhiteSpace(text))
                    {
                        // Every block was non-text (e.g. only "thinking"), or text blocks were empty.
                        // Treat this the same as an empty response rather than returning blank/null
                        // text as if it were a successful answer.
                        _logger.LogError(
                            "[{CorrelationId}] No usable text block in response (stop_reason={StopReason}, blockTypes={BlockTypes}). Raw={Raw}",
                            correlationId, parsed.StopReason, string.Join(",", parsed.Content.Select(b => b.Type)),
                            responseJson.Length > 500 ? responseJson[..500] : responseJson);

                        if (attempt < MaxRetries)
                        {
                            var delayMs = 500 * (attempt + 1);
                            await Task.Delay(delayMs, ct);
                            continue;
                        }

                        throw new AnthropicApiException(500, "no_text_content",
                            $"Anthropic returned no usable text content (stop_reason={parsed.StopReason}, " +
                            $"block types: {string.Join(",", nonTextTypes)}). If this model has thinking " +
                            "enabled by default, verify the request is sending thinking:{type:\"disabled\"} " +
                            "or that max_tokens leaves enough room for both thinking and the text response.");
                    }
>>>>>>> origin/main

                    return (text, usage, parsed.Model);
                }

                var stopReason = parsed?.StopReason ?? "null_response";

<<<<<<< HEAD
                // Refusals are deterministic — retrying won't help.
=======
>>>>>>> origin/main
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
<<<<<<< HEAD
                        correlationId, stopReason, attempt + 1, MaxRetries, responseJson.Length > 500 ? responseJson[..500] : responseJson);
=======
                        correlationId, stopReason, attempt + 1, MaxRetries, delayMs, responseJson.Length > 500 ? responseJson[..500] : responseJson);
>>>>>>> origin/main
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

<<<<<<< HEAD
        // ─────────────────────────────────────────────────────────────────────────
        // Private helpers
        // ─────────────────────────────────────────────────────────────────────────

=======
>>>>>>> origin/main
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

<<<<<<< HEAD
            // Map to domain exceptions
=======
>>>>>>> origin/main
            switch (response.StatusCode)
            {
                case HttpStatusCode.Unauthorized:
                case HttpStatusCode.Forbidden:
                    throw new AnthropicAuthException(
                        $"Anthropic authentication failed ({statusCode}): {errorMessage}");

                case HttpStatusCode.TooManyRequests:
<<<<<<< HEAD
                case (HttpStatusCode)529:   // Anthropic overloaded
=======
                case (HttpStatusCode)529:
>>>>>>> origin/main
                    throw new AnthropicRateLimitException(statusCode,
                        $"Anthropic rate limit / overload ({statusCode}): {errorMessage}");

                default:
                    throw new AnthropicApiException(statusCode, errorType,
                        $"Anthropic API error ({statusCode}) [{errorType}]: {errorMessage}");
            }
        }
    }
<<<<<<< HEAD
}
=======
}
>>>>>>> origin/main
