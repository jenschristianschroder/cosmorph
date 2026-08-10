using System.Globalization;
using Cosmorph.Api.Endpoints;
using Cosmorph.Application.Serialization;
using Cosmorph.Infrastructure.Configuration;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.Logging.ClearProviders();
builder.Logging.AddSimpleConsole(o =>
{
    o.SingleLine = true;
    o.UseUtcTimestamp = true;
});

builder.WebHost.ConfigureKestrel(options =>
{
    // Spectator reads carry no body and mutations are small typed commands.
    options.Limits.MaxRequestBodySize = 16 * 1024;
    options.AddServerHeader = false;
});

builder.Services.AddCosmorph(builder.Configuration, builder.Environment.IsProduction());
builder.Services.AddProblemDetails();
builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;
    options.SerializerOptions.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
    options.SerializerOptions.Converters.Add(new WorldIdJsonConverter());
    options.SerializerOptions.Converters.Add(new WardenIdJsonConverter());
    options.SerializerOptions.Converters.Add(new SpeciesIdJsonConverter());
});
builder.Services.Configure<ForwardedHeadersOptions>(options =>
    options.ForwardedHeaders = ForwardedHeaders.XForwardedProto | ForwardedHeaders.XForwardedFor);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 240,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0,
            }));
});

var app = builder.Build();

app.UseForwardedHeaders();
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseRateLimiter();

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-Frame-Options"] = "DENY";
    headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data: blob:; script-src 'self'; style-src 'self'; connect-src 'self'; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
    await next().ConfigureAwait(false);
});

app.UseDefaultFiles();
app.UseStaticFiles(new StaticFileOptions
{
    OnPrepareResponse = context =>
        context.Context.Response.Headers.CacheControl =
            context.File.Name.Equals("index.html", StringComparison.OrdinalIgnoreCase)
                ? "no-cache"
                : "public, max-age=" + TimeSpan.FromDays(7).TotalSeconds.ToString(CultureInfo.InvariantCulture),
});

app.MapHealthEndpoints();
app.MapSpectatorEndpoints();
app.MapMutationEndpoints(app.Environment.IsProduction());
app.MapFallbackToFile("index.html");

await DemoWorlds.SeedAsync(app.Services).ConfigureAwait(false);

await app.RunAsync().ConfigureAwait(false);

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;
