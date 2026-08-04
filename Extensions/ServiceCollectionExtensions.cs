using AIWebservice.Configuration;
using AIWebservice.Filters;
<<<<<<< HEAD
=======
using AIWebservice.Repositories;
>>>>>>> origin/main
using AIWebservice.Services;
using Microsoft.AspNetCore.RateLimiting;
using System.Threading.RateLimiting;

namespace AIWebservice.Extensions
{
    public static class ServiceCollectionExtensions
    {

        public static IServiceCollection AddLimsServices(
            this IServiceCollection services,
            IConfiguration configuration)
        {
            services.Configure<AnthropicSettings>(
                configuration.GetSection(AnthropicSettings.SectionName));

<<<<<<< HEAD
            services.AddHttpClient<ClaudeService>();

            services.AddSingleton<PromptTemplateService>();
            services.AddScoped<LimsProcessingService>();
=======
            services.Configure<CacheSettings>(
                configuration.GetSection(CacheSettings.SectionName));

            services.AddHttpClient<ClaudeService>();
            services.AddHttpClient<AnthropicBillingService>();
            services.AddSingleton<PromptTemplateService>();
            services.AddScoped<AIReviewProcessingService>();
            services.AddScoped<ReportReviewService>();
            services.AddScoped<RegNoReviewRepository>();
            services.AddScoped<RegNoReviewService>();
>>>>>>> origin/main

            services.AddMemoryCache();

            services.AddRateLimiter(opts =>
            {
                opts.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

                opts.AddSlidingWindowLimiter("claude_api", limiterOpts =>
                {
                    limiterOpts.Window = TimeSpan.FromMinutes(1);
                    limiterOpts.PermitLimit = 60;
                    limiterOpts.SegmentsPerWindow = 6;   // 10-second buckets
                    limiterOpts.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
                    limiterOpts.QueueLimit = 10;
                });
            });

            services.AddControllers(opts =>
            {
                opts.Filters.Add<ValidationFilter>();
            });

            services.Configure<Microsoft.AspNetCore.Mvc.ApiBehaviorOptions>(opts =>
            {
                opts.SuppressModelStateInvalidFilter = true;
            });

            // ── 7. Swagger / OpenAPI ──────────────────────────────────────────────
            services.AddEndpointsApiExplorer();
            services.AddSwaggerGen(opts =>
            {
                opts.SwaggerDoc("v1", new()
                {
                    Title = "LIMS AI Middleware",
                    Version = "v1",
                    Description = "AI-powered data verification middleware for LIMS applications, " +
                                  "powered by Anthropic Claude.",
                });
            });

            return services;
        }
    }

}
