// file name: ReportReviewController.cs
using AIWebservice.Models;
using AIWebservice.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace AIWebservice.Controllers
{
    [ApiController]
    [Route("api/pdf-review")]
    [EnableRateLimiting("claude_api")]
    [Produces("application/json")]
    public sealed class ReportReviewController : ControllerBase
    {
        private readonly ReportReviewService _service;
        private readonly ILogger<ReportReviewController> _logger;

        public ReportReviewController(
            ReportReviewService service,
            ILogger<ReportReviewController> logger)
        {
            _service = service;
            _logger = logger;
        }

        /// <summary>
        /// Uploads one or more PDF reports to Anthropic via the Files API (no base64) and
        /// asks Claude to review them according to the supplied prompt. Send as
        /// multipart/form-data with one or more <c>files</c> parts plus a <c>prompt</c> field.
        /// </summary>
        [HttpPost("process")]
        [Consumes("multipart/form-data")]
        [RequestSizeLimit(350L * 1024 * 1024)]   // 10 files * 32 MB + headroom
        [RequestFormLimits(MultipartBodyLengthLimit = 350L * 1024 * 1024)]
        [ProducesResponseType(typeof(PdfReviewResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status504GatewayTimeout)]
        public async Task<IActionResult> Process(
            [FromForm] PdfReviewRequest request,
            CancellationToken ct)
        {
            var correlationId = string.IsNullOrWhiteSpace(request.CorrelationId)
                ? Guid.NewGuid().ToString("N")
                : request.CorrelationId;
            Response.Headers["X-Correlation-Id"] = correlationId;

            if (request.Files is null || request.Files.Count == 0)
            {
                return BadRequest(new ErrorResponse
                {
                    CorrelationId = correlationId,
                    ErrorCode = "VALIDATION_ERROR",
                    Message = "At least one PDF file must be uploaded in the 'files' form field.",
                });
            }

            _logger.LogInformation(
                "[{CorrelationId}] POST /api/pdf-review/process files={Count}",
                correlationId, request.Files.Count);

            try
            {
                var enriched = new PdfReviewRequest
                {
                    Files = request.Files,
                    Prompt = request.Prompt,
                    SystemPrompt = request.SystemPrompt,
                    ModelOverride = request.ModelOverride,
                    MaxTokensOverride = request.MaxTokensOverride,
                    CorrelationId = correlationId,
                    DeleteFilesAfter = request.DeleteFilesAfter,
                };

                var result = await _service.ReviewAsync(enriched, ct);
                return Ok(result);
            }
            catch (ArgumentException ex)
            {
                return BadRequest(new ErrorResponse
                {
                    CorrelationId = correlationId,
                    ErrorCode = "VALIDATION_ERROR",
                    Message = ex.Message,
                });
            }
        }

        [HttpGet("health")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [DisableRateLimiting]
        public IActionResult Health()
            => Ok(new { status = "healthy", utc = DateTimeOffset.UtcNow });
    }
}
