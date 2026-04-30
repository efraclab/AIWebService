namespace AIWebservice.Models
{
    public sealed class AnthropicApiException : Exception
    {
        public int StatusCode { get; }
        public string ErrorType { get; }

        public AnthropicApiException(int statusCode, string errorType, string message)
            : base(message)
        {
            StatusCode = statusCode;
            ErrorType = errorType;
        }
    }

    public sealed class AnthropicAuthException : Exception
    {
        public AnthropicAuthException(string message) : base(message) { }
    }

    public sealed class AnthropicRateLimitException : Exception
    {
        public int StatusCode { get; }

        public AnthropicRateLimitException(int statusCode, string message)
            : base(message) => StatusCode = statusCode;
    }

    public sealed class AnthropicTimeoutException : Exception
    {
        public AnthropicTimeoutException(string message) : base(message) { }
        public AnthropicTimeoutException(string message, Exception inner) : base(message, inner) { }
    }

    public sealed class UnknownOperationException : Exception
    {
        public string Operation { get; }

        public UnknownOperationException(string operation)
            : base($"No prompt template registered for operation '{operation}'. " +
                   "Supply a systemPrompt in the request or register the operation.")
            => Operation = operation;
    }
}
