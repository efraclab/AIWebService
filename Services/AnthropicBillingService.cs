// file name: AnthropicBillingService.cs
using AIWebservice.Configuration;
using AIWebservice.Models;
using Microsoft.Extensions.Options;
using System.Net.Http.Headers;
using System.Text.Json;

namespace AIWebservice.Services
{
    /// <summary>
    /// Calls the Anthropic Admin API (Usage and Cost API).
    ///
    /// IMPORTANT: Anthropic does NOT have a credit balance endpoint.
    /// The console balance is not exposed via API.
    /// What IS available: /v1/organizations/cost_report — actual spend for a time window.
    ///
    /// Requires Anthropic:AdminApiKey (sk-ant-admin-...) and an Organisation account.
    /// Does NOT work for individual (personal) Anthropic accounts.
    /// </summary>
    public sealed class AnthropicBillingService
    {
        private readonly HttpClient _http;
        private readonly AnthropicSettings _settings;
        private readonly ILogger<AnthropicBillingService> _logger;

        public AnthropicBillingService(
            HttpClient http,
            IOptions<AnthropicSettings> settings,
            ILogger<AnthropicBillingService> logger)
        {
            _http = http;
            _settings = settings.Value;
            _logger = logger;

            _http.BaseAddress = new Uri(_settings.BaseUrl);
            _http.Timeout = TimeSpan.FromSeconds(30);
            _http.DefaultRequestHeaders.Add("x-api-key", _settings.AdminApiKey);
            _http.DefaultRequestHeaders.Add("anthropic-version", _settings.ApiVersion);
            _http.DefaultRequestHeaders.Accept.Add(
                new MediaTypeWithQualityHeaderValue("application/json"));
        }

        /// <summary>
        /// Returns today's total spend in USD using GET /v1/organizations/cost_report.
        /// Data appears within ~5 minutes of API usage.
        /// </summary>
        public async Task<UsageCostSnapshot> GetTodayCostAsync(
            string? correlationId = null,
            CancellationToken ct = default)
        {
            if (string.IsNullOrWhiteSpace(_settings.AdminApiKey))
            {
                _logger.LogWarning(
                    "[{CorrelationId}] AdminApiKey not configured — cost data unavailable. " +
                    "Add Anthropic:AdminApiKey (sk-ant-admin-...) and ensure account has an Organisation.",
                    correlationId);
                return UsageCostSnapshot.Unavailable;
            }

            var start = DateTime.UtcNow.Date;
            var end = DateTime.UtcNow;

            var startingAt = Uri.EscapeDataString(start.ToString("yyyy-MM-ddTHH:mm:ssZ"));
            var endingAt = Uri.EscapeDataString(end.ToString("yyyy-MM-ddTHH:mm:ssZ"));

            var url =
                    $"/v1/organizations/analytics/cost_report" +
                    $"?starting_at={startingAt}" +
                    $"&ending_at={endingAt}" +
                    $"&bucket_width=1d";

            _logger.LogInformation("Cost API URL = {Url}", url);

            using var request = new HttpRequestMessage(HttpMethod.Get, url);

            HttpResponseMessage response;
            try
            {
                response = await _http.SendAsync(request, ct);
            }
            catch (TaskCanceledException ex) when (!ct.IsCancellationRequested)
            {
                throw new AnthropicTimeoutException("Anthropic cost API timed out.", ex);
            }
            catch (HttpRequestException ex)
            {
                throw new AnthropicApiException(0, "network_error",
                    $"Network error contacting Anthropic cost API: {ex.Message}");
            }

            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(ct);
                _logger.LogError("[{CorrelationId}] Cost API {Status}: {Body}",
                    correlationId, (int)response.StatusCode, body);
                throw new AnthropicApiException(
                    (int)response.StatusCode, "billing_error",
                    $"Anthropic cost API error ({(int)response.StatusCode}): {body}");
            }

            var json = await response.Content.ReadAsStringAsync(ct);
            var snapshot = ParseCostReport(json, correlationId);

            _logger.LogInformation(
                "[{CorrelationId}] Cost today={Cost} {Currency} | {Start}→{End}",
                correlationId, snapshot.TodayCostUsd, snapshot.Currency,
                snapshot.PeriodStart, snapshot.PeriodEnd);

            return snapshot;
        }

        private UsageCostSnapshot ParseCostReport(string json, string? correlationId)
        {
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var totalCost = 0m;
                var currency = "USD";
                var periodStart = string.Empty;
                var periodEnd = string.Empty;

                if (root.TryGetProperty("data", out var data) &&
                    data.ValueKind == JsonValueKind.Array)
                {
                    foreach (var bucket in data.EnumerateArray())
                    {
                        if (periodStart == string.Empty &&
                            bucket.TryGetProperty("starting_at", out var s))
                            periodStart = s.GetString() ?? string.Empty;

                        if (bucket.TryGetProperty("ending_at", out var e))
                            periodEnd = e.GetString() ?? string.Empty;

                        if (bucket.TryGetProperty("results", out var results) &&
                            results.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in results.EnumerateArray())
                            {
                                // amount is a string like "123.78912"
                                if (item.TryGetProperty("amount", out var amt) &&
                                    decimal.TryParse(
                                        amt.GetString(),
                                        System.Globalization.NumberStyles.Any,
                                        System.Globalization.CultureInfo.InvariantCulture,
                                        out var parsed))
                                    totalCost += parsed;

                                if (item.TryGetProperty("currency", out var cur))
                                    currency = cur.GetString() ?? "USD";
                            }
                        }
                    }
                }

                return new UsageCostSnapshot
                {
                    TodayCostUsd = Math.Round(totalCost, 6),
                    Currency = currency,
                    PeriodStart = periodStart,
                    PeriodEnd = periodEnd,
                    IsAvailable = true,
                };
            }
            catch (JsonException ex)
            {
                _logger.LogError(ex,
                    "[{CorrelationId}] Failed to parse cost report", correlationId);
                return UsageCostSnapshot.Unavailable;
            }
        }
    }
}