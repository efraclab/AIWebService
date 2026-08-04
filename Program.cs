<<<<<<< HEAD
using AIWebservice.Extensions;
using AIWebservice.Middleware;
=======
// file name: Program.cs
using AIWebservice.Extensions;
using AIWebservice.Middleware;
using AIWebservice.Services;          // ← fixes "AnthropicBillingService could not be found"
>>>>>>> origin/main
using Serilog;
using Serilog.Events;

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.File(
        path: "logs/lims-ai-.log",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateBootstrapLogger();

<<<<<<< HEAD

=======
>>>>>>> origin/main
try
{
    Log.Information("Starting LIMS AI Middleware...");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, services, cfg) =>
        cfg.ReadFrom.Configuration(ctx.Configuration)
           .ReadFrom.Services(services)
           .Enrich.FromLogContext()
           .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
           .MinimumLevel.Override("System", LogEventLevel.Warning)
           .WriteTo.Console(outputTemplate:
               "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
           .WriteTo.File(
               path: "logs/lims-ai-.log",
               rollingInterval: RollingInterval.Day,
               retainedFileCountLimit: 14));

    builder.Services.AddLimsServices(builder.Configuration);
<<<<<<< HEAD
=======
    // ↑ AnthropicBillingService is already registered inside AddLimsServices —
    //   the duplicate builder.Services.AddHttpClient<AnthropicBillingService>()
    //   that was here has been removed.
>>>>>>> origin/main

    builder.Services.AddCors(options =>
    {
        options.AddPolicy("AllowReactApp",
            policy =>
            {
                policy
                    .AllowAnyOrigin()
                    .AllowAnyHeader()
                    .AllowAnyMethod();
            });
    });

    var app = builder.Build();

    app.UseMiddleware<GlobalExceptionMiddleware>();
<<<<<<< HEAD
    
=======

>>>>>>> origin/main
    app.UseSerilogRequestLogging(opts =>
    {
        opts.MessageTemplate =
            "HTTP {RequestMethod} {RequestPath} responded {StatusCode} in {Elapsed:0.000} ms";
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI(opts =>
        {
            opts.SwaggerEndpoint("/swagger/v1/swagger.json", "LIMS AI Middleware v1");
<<<<<<< HEAD
            opts.RoutePrefix = string.Empty;   // Serve Swagger at root "/"
        });
    }

    app.UseCors("AllowReactApp");

    app.UseHttpsRedirection();
    app.UseRateLimiter();
=======
            opts.RoutePrefix = string.Empty;
        });
    }

    //app.UseHttpsRedirection();

    app.UseCors("AllowReactApp");

    app.UseRateLimiter();

>>>>>>> origin/main
    app.MapControllers();

    Log.Information("LIMS AI Middleware started. Listening on {Urls}",
        string.Join(", ", app.Urls.DefaultIfEmpty("default ports")));

    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "LIMS AI Middleware terminated unexpectedly during startup.");
}
finally
{
    await Log.CloseAndFlushAsync();
<<<<<<< HEAD
}
=======
}
>>>>>>> origin/main
