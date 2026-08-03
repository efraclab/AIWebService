// file name: RegNoReviewController.cs
using AIWebservice.Models;
using AIWebservice.Models.coa;
using AIWebservice.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AIWebservice.Controllers
{
    [ApiController]
    [Route("api/find")]
    [EnableRateLimiting("claude_api")]
    [Produces("application/json")]
    public sealed class RegNoReviewController : ControllerBase
    {
        private readonly RegNoReviewService _service;
        private readonly AnthropicBillingService _billing;
        private readonly ILogger<RegNoReviewController> _logger;

        public RegNoReviewController(
            RegNoReviewService service,
            AnthropicBillingService billing,
            ILogger<RegNoReviewController> logger)
        {
            _service = service;
            _billing = billing;
            _logger = logger;
        }

        /// Fetches CoA rows for the given regNo from the LIMS database and asks Claude to review
        /// them using the supplied prompt. The DB data and the AI review are returned together.
        [HttpPost("fetch-review")]
        [ProducesResponseType(typeof(RegNoReviewFetchReviewResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status504GatewayTimeout)]
        public async Task<IActionResult> FetchAndReview(
            [FromBody] RegNoFetchReviewRequest request,
            CancellationToken ct)
        {
            _logger.LogInformation(
                "POST /api/find/fetch-review regNo={RegNo}", request.RegNo);

            var result = await _service.FetchAndReviewAsync(request, ct);
            Response.Headers["X-Correlation-Id"] = result.CorrelationId;
            return Ok(result);
        }

        /// Updates header fields (Trn105) and/or one or more detail rows (Trn205) for the
        /// given regNo. Only fields explicitly supplied in the request body are written —
        /// omitted or null fields are left untouched. No AI involvement.
        /// Every changed field is automatically recorded in AiReportAuditLog.
        /// Returns per-item outcomes including skipped rows and affected row counts.
        [HttpPut("update")]
        [ProducesResponseType(typeof(RegNoReviewUpdateResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status422UnprocessableEntity)]
        public async Task<IActionResult> Update(
            [FromBody] RegNoReviewUpdateRequest request,
            CancellationToken ct)
        {
            _logger.LogInformation(
                "PUT /api/find/update regNo={RegNo} | headerProvided={HasHeader} | items={Count}",
                request.RegNo,
                request.Header is not null,
                request.Items?.Count ?? 0);

            var hasHeader = request.Header is not null && request.Header.HasAnyValue();
            var hasItems = request.Items is { Count: > 0 };

            if (!hasHeader && !hasItems)
            {
                return UnprocessableEntity(new ErrorResponse
                {
                    Message = "The request must include either a 'header' object with at least one " +
                              "non-null field, or an 'items' array with at least one entry."
                });
            }

            var result = await _service.UpdateAsync(request, ct);

            var skippedCount = result.ItemResults.Count(r => r.Skipped);
            if (skippedCount > 0)
                Response.Headers["X-Skipped-Items"] = skippedCount.ToString();

            return Ok(result);
        }

        /// Returns the current approval status for a regNo.
        /// 404 is returned when no approval decision has been recorded yet.
        [ProducesResponseType(typeof(RegNoReviewApprovalRecord), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status404NotFound)]
        [HttpGet("approval")]
        public async Task<IActionResult> GetApproval([FromQuery] string regNo, CancellationToken ct)
        {
            _logger.LogInformation("GET /api/find/approval/{RegNo}", regNo);

            var record = await _service.GetApprovalAsync(regNo, ct);
            if (record is null)
            {
                return NotFound(new ErrorResponse
                {
                    Message = $"No approval record found for regNo '{regNo}'. " +
                               "The report has not been reviewed yet (implicitly Pending)."
                });
            }

            return Ok(record);
        }

        /// Creates or updates the approval status for a regNo.
        /// Allowed values for Status: Approved | Rejected | Pending.
        /// The action is automatically written to AiReportAuditLog as a StatusChange entry.
        [HttpPost("approval")]
        [ProducesResponseType(typeof(RegNoReviewApprovalRecord), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> SetApproval(
            [FromBody] RegNoReviewSetApprovalRequest request,
            CancellationToken ct)
        {
            _logger.LogInformation(
                "POST /api/find/approval regNo={RegNo} status={Status}",
                request.RegNo, request.Status);

            if (string.IsNullOrWhiteSpace(request.RegNo))
                return BadRequest(new ErrorResponse { Message = "'regNo' is required." });

            if (string.IsNullOrWhiteSpace(request.ReviewedBy))
                return BadRequest(new ErrorResponse { Message = "'reviewedBy' is required." });

            var validStatuses = new[] { "Approved", "Rejected", "Pending" };
            if (!validStatuses.Contains(request.Status, StringComparer.OrdinalIgnoreCase))
            {
                return BadRequest(new ErrorResponse
                {
                    Message = $"'status' must be one of: {string.Join(", ", validStatuses)}."
                });
            }

            var record = await _service.SetApprovalAsync(request, ct);
            return Ok(record);
        }

        /// Returns the complete field-level audit trail for a regNo, newest-first.
        /// Includes DetailUpdate entries (which field on which row changed),
        /// HeaderUpdate entries, and StatusChange entries.
        /// regNo is sent in the request body to avoid slash-encoding issues in routing.
        [HttpPost("audit")]
        [ProducesResponseType(typeof(IReadOnlyList<AuditLogEntry>), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> GetAuditLogs(
            [FromBody] AuditLogRequest request,
            CancellationToken ct)
        {
            _logger.LogInformation("POST /api/find/audit regNo={RegNo}", request.RegNo);

            if (string.IsNullOrWhiteSpace(request.RegNo))
                return BadRequest(new ErrorResponse { Message = "'regNo' body field is required." });

            var logs = await _service.GetAuditLogsAsync(request.RegNo, ct);
            return Ok(logs);
        }

        // ─────────────────────────────────────────────────────────────────────────
        // Billing — balance snapshot (debug / monitoring)
        // ─────────────────────────────────────────────────────────────────────────

        /// Returns the current Anthropic credit balance using the Admin API key.
        /// Requires Anthropic:AdminApiKey (sk-ant-admin-...) to be configured.
        /// balanceAvailable = false means the Admin key is missing — all values will be 0.
        [HttpGet("balance")]
        [DisableRateLimiting]
        [ProducesResponseType(typeof(UsageCostSnapshot), StatusCodes.Status200OK)]
        public async Task<IActionResult> GetBalance(CancellationToken ct)
        {
            _logger.LogInformation("GET /api/find/balance");
            var snapshot = await _billing.GetTodayCostAsync("balance-check", ct);
            return Ok(snapshot);
        }

    }
}