using AIWebservice.Models;
using AIWebservice.Models.coa;
using AIWebservice.Repositories;
using System.Text.Json;
using AIWebservice.Configuration;

namespace AIWebservice.Services
{
    public sealed class RegNoReviewService
    {
        private const string DefaultSystemPrompt =
            "You are an expert QC/CoA (Certificate of Analysis) reviewer for a pharmaceutical " +
            "and cosmetics laboratory operating under Indian pharmaceutical standards. The user " +
            "will provide CoA test rows (parameter, method, requirement, result, etc.) for a " +
            "single registration number along with a prompt describing what to analyse. Base your " +
            "answer strictly on the supplied rows; do not invent values. When citing a finding, " +
            "reference the parameter and group code so the analyst can locate the row.";

        // Deterministic rule-checking, not creative generation — a low/zero temperature
        // reduces (but does not eliminate) run-to-run variance in which rule violations
        // get surfaced against the same data.
        private const double ReviewTemperature = 0.0;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false,
        };

        private readonly RegNoReviewRepository _repo;
        private readonly ClaudeService _claude;
        private readonly ILogger<RegNoReviewService> _logger;

        public RegNoReviewService(
            RegNoReviewRepository repo,
            ClaudeService claude,
            ILogger<RegNoReviewService> logger)
        {
            _repo = repo;
            _claude = claude;
            _logger = logger;
        }

        // ── Fetch + AI review ─────────────────────────────────────────────────

        public async Task<RegNoReviewFetchReviewResponse> FetchAndReviewAsync(
            RegNoFetchReviewRequest request,
            CancellationToken ct = default)
        {
            var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? Guid.NewGuid().ToString("N")
                : request.CorrelationId;

            _logger.LogInformation(
                "[{CorrelationId}] CoA fetch+review regNo={RegNo}",
                correlationId, request.RegNo);

            var rows = await _repo.GetByRegNoAsync(request.RegNo, ct);
            if (rows.Count == 0)
            {
                return new RegNoReviewFetchReviewResponse
                {
                    CorrelationId = correlationId,
                    RegNo = request.RegNo,
                    RowCount = 0,
                    Data = [],
                    Review = $"No CoA rows were found for regNo '{request.RegNo}'.",
                    Model = string.Empty,
                };
            }

            var dataJson = JsonSerializer.Serialize(rows, _jsonOpts);

            var systemPrompt = string.IsNullOrWhiteSpace(request.SystemPrompt)
                ? DefaultSystemPrompt
                : request.SystemPrompt;

            var userMessage = $"""
            {request.Prompt}

            [DOCUMENT CONTEXT] The JSON below is a Certificate of Analysis (CoA) record fetched from the LIMS database for registration number '{request.RegNo}'. Each entry is one tested parameter and its result.

            --- DATA ---
            {dataJson}
            """;

            var (review, usage, modelUsed) = await _claude.SendAsync(
                systemPrompt: systemPrompt,
                userMessage: userMessage,
                model: request.ModelOverride,
                maxTokens: request.MaxTokensOverride,
                temperature: ReviewTemperature,
                correlationId: correlationId,
                ct: ct);

            var estimatedCost = AnthropicPricing.Calculate(
                usage.InputTokens,
                usage.OutputTokens,
                usage.CacheCreationInputTokens,
                usage.CacheReadInputTokens);

            _logger.LogInformation(
                "[{CorrelationId}] CoA review completed | rows={RowCount} | model={Model} | tokens={Total}",
                correlationId, rows.Count, modelUsed, usage.InputTokens + usage.OutputTokens);

            return new RegNoReviewFetchReviewResponse
            {
                CorrelationId = correlationId,
                RegNo = request.RegNo,
                RowCount = rows.Count,
                Data = rows,
                Review = review,
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
            };
        }

        // ── Update ────────────────────────────────────────────────────────────

        public async Task<RegNoReviewUpdateResponse> UpdateAsync(
            RegNoReviewUpdateRequest request,
            CancellationToken ct = default)
        {
            var items = request.Items ?? [];

            _logger.LogInformation(
                "CoA update regNo={RegNo} | headerProvided={HasHeader} | detailItems={Count} | changedBy={ChangedBy}",
                request.RegNo,
                request.Header is not null,
                items.Count,
                request.ChangedBy ?? "(anonymous)");

            if (request.Header is null && items.Count == 0)
            {
                _logger.LogWarning(
                    "CoA update regNo={RegNo} rejected — no header and no detail items supplied.",
                    request.RegNo);

                return new RegNoReviewUpdateResponse
                {
                    RegNo = request.RegNo,
                    Success = false,
                    HeaderRowsAffected = 0,
                    DetailRowsAffected = 0,
                    ItemResults = [],
                };
            }

            // Pass ChangedBy through to the repository so it gets stamped on every audit log row
            var response = await _repo.UpdateAsync(request.RegNo, request.Header, items, request.ChangedBy, ct);

            _logger.LogInformation(
                "CoA update regNo={RegNo} complete | headerRows={Header} | detailRows={Detail} | skipped={Skipped}",
                request.RegNo,
                response.HeaderRowsAffected,
                response.DetailRowsAffected,
                response.ItemResults.Count(r => r.Skipped));

            return response;
        }

        // ── Approval status ───────────────────────────────────────────────────

        /// <summary>
        /// Returns the current approval status for a regNo.
        /// Returns null if no approval record exists yet (i.e. still implicitly Pending).
        /// </summary>
        public Task<RegNoReviewApprovalRecord?> GetApprovalAsync(string regNo, CancellationToken ct = default)
        {
            _logger.LogInformation("CoA approval fetch regNo={RegNo}", regNo);
            return _repo.GetApprovalAsync(regNo, ct);
        }

        /// <summary>
        /// Sets the approval status (Approved / Rejected / Pending) for a regNo.
        /// Creates the record on first call, updates it on subsequent calls.
        /// </summary>
        public async Task<RegNoReviewApprovalRecord> SetApprovalAsync(
            RegNoReviewSetApprovalRequest request,
            CancellationToken ct = default)
        {
            _logger.LogInformation(
                "CoA approval set regNo={RegNo} | status={Status} | by={ReviewedBy}",
                request.RegNo, request.Status, request.ReviewedBy);

            var record = await _repo.UpsertApprovalAsync(
                request.RegNo,
                request.Status,
                request.ReviewedBy,
                request.Notes,
                ct);

            _logger.LogInformation(
                "CoA approval regNo={RegNo} → {Status} by {ReviewedBy}",
                record.RegNo, record.Status, record.ReviewedBy);

            return record;
        }

        // ── Audit log ─────────────────────────────────────────────────────────

        /// <summary>
        /// Returns the full field-level audit trail for a regNo, newest-first.
        /// </summary>
        public async Task<IReadOnlyList<AuditLogEntry>> GetAuditLogsAsync(
            string regNo, CancellationToken ct = default)
        {
            _logger.LogInformation("CoA audit log fetch regNo={RegNo}", regNo);
            return await _repo.GetAuditLogsAsync(regNo, ct);
        }
    }
}