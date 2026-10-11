using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

// OpenAPI document generation (built into .NET)
builder.Services.AddOpenApi();

// Inject a clock so we can fake it in tests later
builder.Services.AddSingleton(TimeProvider.System);

var app = builder.Build();

// OpenAPI JSON at /openapi/v1.json, Scalar UI on top of it
app.MapOpenApi();
app.MapScalarApiReference();

app.MapGet("/", () => Results.Redirect("/scalar"))
   .ExcludeFromDescription();

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
app.MapGet("/api/time/{**timeZoneId}", (string timeZoneId, TimeProvider clock) =>
{
    TimeZoneInfo tz;
    try
    {
        tz = TimeZoneInfo.FindSystemTimeZoneById(timeZoneId);
    }
    catch (TimeZoneNotFoundException)
    {
        return Results.NotFound(new { error = $"Time zone '{timeZoneId}' was not found." });
    }

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