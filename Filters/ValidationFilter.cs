using AIWebservice.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using System.Text.Json;

namespace AIWebservice.Filters
{
    public sealed class ValidationFilter : IActionFilter
    {
        private static readonly JsonSerializerOptions _jsonOpts = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        };

        public void OnActionExecuting(ActionExecutingContext context)
        {
            if (context.ModelState.IsValid) return;

            var errors = context.ModelState
                .Where(kv => kv.Value?.Errors.Count > 0)
                .ToDictionary(
                    kv => kv.Key,
                    kv => kv.Value!.Errors.Select(e => e.ErrorMessage).ToArray()
                );

            var correlationId = context.HttpContext.Request.Headers
                .TryGetValue("X-Correlation-Id", out var cid)
                ? cid.ToString()
                : context.HttpContext.TraceIdentifier;

            var error = new ErrorResponse
            {
                CorrelationId = correlationId,
                ErrorCode = "VALIDATION_ERROR",
                Message = "One or more request fields failed validation.",
                ValidationErrors = errors,
            };

            context.Result = new BadRequestObjectResult(error);
        }

        public void OnActionExecuted(ActionExecutedContext context) { /* no-op */ }
    }
}
