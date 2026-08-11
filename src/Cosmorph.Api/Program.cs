using System.Globalization;
using Cosmorph.Api.Authentication;
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

// Mutations are the only authenticated surface. A production API that cannot authenticate anyone
// refuses to start rather than serving every mutation as a permanent 501.
var authentication = CosmorphOptions.FromConfiguration(builder.Configuration).Authentication;
authentication.ValidateForEnvironment(builder.Environment.IsProduction());
builder.Services.AddMutationAuthentication(authentication);

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.PropertyNamingPolicy = System.Text.Json.JsonNamingPolicy.CamelCase;
    options.SerializerOptions.DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull;

    // Untrusted world names and generated narration are data. The HTML-safe encoder keeps them
    // inert even if a response is ever embedded somewhere other than a JSON parser.
    options.SerializerOptions.Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Default;
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

if (authentication.IsConfigured)
{
    app.UseAuthentication();
    app.UseAuthorization();
}

app.Use(async (context, next) =>
{
    var headers = context.Response.Headers;
    headers["X-Content-Type-Options"] = "nosniff";
    headers["Referrer-Policy"] = "no-referrer";
    headers["X-Frame-Options"] = "DENY";

    // Sign-in needs the directory reachable for one thing only: the browser POSTs its authorization
    // code to the token endpoint. The flow is a full-page redirect with no hidden frame, so the
    // directory is allowed in connect-src and nowhere else, and only when sign-in exists at all.
    var signIn = authentication.IsConfigured ? " https://login.microsoftonline.com" : string.Empty;
    headers["Content-Security-Policy"] =
        "default-src 'self'; img-src 'self' data: blob:; script-src 'self'; style-src 'self'; connect-src 'self'"
        + signIn + "; frame-src 'none'"
        + "; object-src 'none'; base-uri 'none'; frame-ancestors 'none'";
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
app.MapConfigEndpoints();
app.MapSpectatorEndpoints();
app.MapMutationEndpoints(app.Environment.IsProduction(), authentication);
app.MapFallbackToFile("index.html");

await DemoWorlds.SeedAsync(app.Services).ConfigureAwait(false);

await app.RunAsync().ConfigureAwait(false);

/// <summary>Exposed so integration tests can host the API with WebApplicationFactory.</summary>
public partial class Program;
