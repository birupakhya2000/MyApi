using Scalar.AspNetCore;
using Azure.Monitor.OpenTelemetry.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

if(!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}
// OpenAPI document generation (built into .NET)
builder.Services.AddOpenApi();

// Inject a clock so we can fake it in tests later
builder.Services.AddSingleton(TimeProvider.System);

builder.Services.AddHealthChecks();

var app = builder.Build();

var enableDocs = app.Environment.IsDevelopment() || app.Configuration.GetValue<bool>("EnableApiDocs");

if(enableDocs)
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}


app.MapGet("/", () => enableDocs ? Results.Redirect("/scalar") : Results.Ok("MyApi is running."))
   .ExcludeFromDescription();

app.MapHealthChecks("/health").ExcludeFromDescription();    

// GET /api/time -> current time on the server
app.MapGet("/api/time", (TimeProvider clock) =>
{
    var tz = clock.LocalTimeZone;
    var utcNow = clock.GetUtcNow();
    var localNow = TimeZoneInfo.ConvertTime(utcNow, tz);

    return Results.Ok(new TimeResponse(
        UtcNow: utcNow,
        LocalNow: localNow,
        TimeZoneId: tz.Id,
        TimeZoneName: tz.DisplayName,
        UtcOffset: localNow.Offset.ToString()));
})
.WithName("GetCurrentTime")
.WithSummary("Gets the current server time and time zone")
.Produces<TimeResponse>(StatusCodes.Status200OK);

// GET /api/time/{timeZoneId} -> current time in a given time zone
app.MapGet("/api/time/{**timeZoneId}", (string timeZoneId, TimeProvider clock, ILogger<Program> logger) =>
{
    TimeZoneInfo tz;
    try
    {
        tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }
    catch (TimeZoneNotFoundException)
    {
        logger.LogWarning("Time zone {TimeZoneId} was not found", timeZoneId);
        return Results.NotFound(new { error = $"Time zone '{timeZoneId}' was not found." });
    }

    logger.LogInformation("Returning time for zone {TimeZoneId}", timeZoneId);

    var utcNow = clock.GetUtcNow();
    var localNow = TimeZoneInfo.ConvertTime(utcNow, tz);

    return Results.Ok(new TimeResponse(
        UtcNow: utcNow,
        LocalNow: localNow,
        TimeZoneId: tz.Id,
        TimeZoneName: tz.DisplayName,
        UtcOffset: localNow.Offset.ToString()));
})
.WithName("GetTimeByTimeZone")
.WithSummary("Gets the current time in the given time zone (e.g. Asia/Kolkata)")
.Produces<TimeResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

app.Run();

public record TimeResponse(
    DateTimeOffset UtcNow,
    DateTimeOffset LocalNow,
    string TimeZoneId,
    string TimeZoneName,
    string UtcOffset);

// Makes Program visible to the test project (WebApplicationFactory) in the next step
public partial class Program;