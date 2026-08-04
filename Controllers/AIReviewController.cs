// file name: AIReviewController.cs
using AIWebservice.Models;
using AIWebservice.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using System.ComponentModel.DataAnnotations;

namespace AIWebservice.Controllers
{
    [ApiController]
    [Route("api/lims")]
    [EnableRateLimiting("claude_api")]
    [Produces("application/json")]
    public sealed class AIReviewController : ControllerBase
    {
        private readonly AIReviewProcessingService _processor;
        private readonly PromptTemplateService _templates;
        private readonly ILogger<AIReviewController> _logger;

        public AIReviewController(
            AIReviewProcessingService processor,
            PromptTemplateService templates,
            ILogger<AIReviewController> logger)
        {
            _processor = processor;
            _templates = templates;
            _logger = logger;
        }


        [HttpPost("process")]
        [ProducesResponseType(typeof(AIReviewResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status429TooManyRequests)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status502BadGateway)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status504GatewayTimeout)]
        public async Task<IActionResult> Process(
            [FromBody] AIReviewRequest request,
            CancellationToken ct)
        {
            var correlationId = SetCorrelationHeader(request.CorrelationId);

            _logger.LogInformation(
                "[{CorrelationId}] POST /api/lims/process operation={Op} source={Source}",
                correlationId, request.Operation, request.Source);

            var result = await _processor.ProcessAsync(request, ct);
            return Ok(result);
        }


        [HttpPost("batch")]
        [ProducesResponseType(typeof(BatchResponse), StatusCodes.Status200OK)]
        [ProducesResponseType(typeof(ErrorResponse), StatusCodes.Status400BadRequest)]
        public async Task<IActionResult> Batch(
            [FromBody] BatchRequest batchRequest,
            CancellationToken ct)
        {
            var correlationId = SetCorrelationHeader(null);

            if (batchRequest.Requests is null || batchRequest.Requests.Count == 0)
                return BadRequest(BuildError(correlationId, "VALIDATION_ERROR",
                    "The 'requests' array must contain at least one item."));

            if (batchRequest.Requests.Count > 20)
                return BadRequest(BuildError(correlationId, "VALIDATION_ERROR",
                    "A maximum of 20 requests may be submitted per batch."));

            _logger.LogInformation(
                "[{CorrelationId}] POST /api/lims/batch count={Count}",
                correlationId, batchRequest.Requests.Count);

            // Throttle concurrent calls to Anthropic
            var semaphore = new SemaphoreSlim(5, 5);
            var tasks = batchRequest.Requests.Select(ProcessSingleBatchItem);

            var results = await Task.WhenAll(tasks);

            return Ok(new BatchResponse
            {
                CorrelationId = correlationId,
                TotalItems = results.Length,
                SuccessCount = results.Count(r => r.Success),
                FailureCount = results.Count(r => !r.Success),
                Items = results,
            });

            // Local function — keeps semaphore lifetime clean
            async Task<BatchItemResult> ProcessSingleBatchItem(AIReviewRequest req)
            {
                await semaphore.WaitAsync(ct);
                try
                {
                    var r = await _processor.ProcessAsync(req, ct);
                    return new BatchItemResult { Success = true, Response = r };
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex,
                        "[{CorrelationId}] Batch item failed: operation={Op}",
                        correlationId, req.Operation);

                    return new BatchItemResult
                    {
                        Success = false,
                        Error = new ErrorResponse
                        {
                            CorrelationId = correlationId,
                            ErrorCode = "BATCH_ITEM_ERROR",
                            Message = ex.Message,
                        },
                    };
                }
                finally
                {
                    semaphore.Release();
                }
            }
        }

        [HttpGet("operations")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        public IActionResult GetOperations()
        {
            var ops = _templates.GetRegisteredOperations();
            return Ok(new { operations = ops });
        }

        [HttpGet("health")]
        [ProducesResponseType(StatusCodes.Status200OK)]
        [DisableRateLimiting]

           // balance check shouldn't consume your claude_api rate limit slots
        public IActionResult Health()
            => Ok(new { status = "healthy", utc = DateTimeOffset.UtcNow });

        [HttpGet("balance")]
        [DisableRateLimiting]
        // ─────────────────────────────────────────────────────────────────────────
        // Helpers
        // ─────────────────────────────────────────────────────────────────────────

        private string SetCorrelationHeader(string? requested)
        {
            var id = string.IsNullOrWhiteSpace(requested)
                ? Guid.NewGuid().ToString("N")
                : requested;

            Response.Headers["X-Correlation-Id"] = id;
            return id;
        }

        private static ErrorResponse BuildError(
            string correlationId, string code, string message)
            => new() { CorrelationId = correlationId, ErrorCode = code, Message = message };
    }

    // ── Batch DTOs ─────────────────────────────────────────────────────────────
    public sealed class BatchRequest
    {
        [Required]
        public IReadOnlyList<AIReviewRequest>? Requests { get; init; }
    }

    public sealed class BatchResponse
    {
        public string CorrelationId { get; init; } = string.Empty;
        public int TotalItems { get; init; }
        public int SuccessCount { get; init; }
        public int FailureCount { get; init; }
        public IReadOnlyList<BatchItemResult> Items { get; init; } = [];
    }

    public sealed class BatchItemResult
    {
        public bool Success { get; init; }
        public AIReviewResponse? Response { get; init; }
        public ErrorResponse? Error { get; init; }
    }
}
