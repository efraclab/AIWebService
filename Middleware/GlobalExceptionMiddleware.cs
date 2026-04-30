using AIWebservice.Models;
using System.Text.Json;

namespace AIWebservice.Middleware
{
    public sealed class GlobalExceptionMiddleware
    {
        private readonly RequestDelegate _next;
        private readonly ILogger<GlobalExceptionMiddleware> _logger;

        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public GlobalExceptionMiddleware(
            RequestDelegate next,
            ILogger<GlobalExceptionMiddleware> logger)
        {
            _next = next;
            _logger = logger;
        }

        public async Task InvokeAsync(HttpContext ctx)
        {
            try
            {
                await _next(ctx);
            }
            catch (Exception ex)
            {
                await HandleExceptionAsync(ctx, ex);
            }
        }

        // ─────────────────────────────────────────────────────────────────────────

        private async Task HandleExceptionAsync(HttpContext ctx, Exception ex)
        {

            var correlationId = ctx.Response.Headers.TryGetValue("X-Correlation-Id", out var cid)
                ? cid.ToString()
                : ctx.TraceIdentifier;

            var (statusCode, errorCode, message) = MapException(ex);

            _logger.LogError(ex,
                "[{CorrelationId}] Unhandled {ExceptionType}: {Message}",
                correlationId, ex.GetType().Name, ex.Message);

            ctx.Response.StatusCode = statusCode;
            ctx.Response.ContentType = "application/json";

            var errorResponse = new ErrorResponse
            {
                CorrelationId = correlationId,
                ErrorCode = errorCode,
                Message = message,
            };

            var json = JsonSerializer.Serialize(errorResponse, _jsonOpts);
            await ctx.Response.WriteAsync(json);
        }

        private static (int StatusCode, string ErrorCode, string Message) MapException(Exception ex)
            => ex switch
            {
                AnthropicAuthException authEx
                    => (StatusCodes.Status502BadGateway,
                        "ANTHROPIC_AUTH_FAILURE",
                        "The service could not authenticate with the AI provider. " +
                        "Contact your system administrator."),

                AnthropicRateLimitException rlEx
                    => (StatusCodes.Status429TooManyRequests,
                        "AI_RATE_LIMIT",
                        "The AI provider is currently rate-limiting requests. " +
                        "Please retry after a short delay."),

                AnthropicTimeoutException
                    => (StatusCodes.Status504GatewayTimeout,
                        "AI_TIMEOUT",
                        "The AI provider did not respond within the allowed time. " +
                        "Please retry your request."),

                AnthropicApiException apiEx
                    => (StatusCodes.Status502BadGateway,
                        $"AI_API_ERROR_{apiEx.ErrorType.ToUpperInvariant()}",
                        $"The AI provider returned an error: {apiEx.Message}"),

                UnknownOperationException opEx
                    => (StatusCodes.Status400BadRequest,
                        "UNKNOWN_OPERATION",
                        opEx.Message),

                OperationCanceledException
                    => (StatusCodes.Status499ClientClosedRequest,    // nginx convention
                        "REQUEST_CANCELLED",
                        "The request was cancelled."),

                _ => (StatusCodes.Status500InternalServerError,
                      "INTERNAL_ERROR",
                      "An unexpected error occurred. Please try again or contact support.")
            };
    }
}
