using System.Text;
using System.Threading.RateLimiting;

using Azure.Monitor.OpenTelemetry.AspNetCore;

using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Cors.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;

using MyApi.Api.Auth;
using MyApi.Api.Data;

using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);

if (!string.IsNullOrWhiteSpace(builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"]))
{
    builder.Services.AddOpenTelemetry().UseAzureMonitor();
}

builder.Services.AddOpenApi();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddHealthChecks();

// --- Database ---
// --- Database: SqlServer by default (Azure), Sqlite for local dev and tests ---
// Read the provider lazily so test/host configuration overrides are honoured
static bool IsSqlite(IConfiguration config) =>
    string.Equals(config["Database:Provider"] ?? "SqlServer", "Sqlite", StringComparison.OrdinalIgnoreCase);

builder.Services.AddDbContext<AppDbContext>((sp, options) =>
{
    var config = sp.GetRequiredService<IConfiguration>();
    var cs = config.GetConnectionString("Default")
             ?? throw new InvalidOperationException("ConnectionStrings:Default is not configured.");

    if (IsSqlite(config))
    {
        options.UseSqlite(cs);
    }
    else
    {
        // Retries cover the free tier's auto-pause: the first connection can fail while it wakes up
        options.UseSqlServer(cs, sql => sql.EnableRetryOnFailure(
            maxRetryCount: 6,
            maxRetryDelay: TimeSpan.FromSeconds(15),
            errorNumbersToAdd: null));
    }
});
builder.Services.AddSingleton<IPasswordHasher<User>, PasswordHasher<User>>();

// --- JWT authentication ---
var jwtKey = builder.Configuration["Jwt:Key"]
    ?? throw new InvalidOperationException("Jwt:Key is not configured.");
var jwtIssuer = builder.Configuration["Jwt:Issuer"] ?? "MyApi";
var jwtAudience = builder.Configuration["Jwt:Audience"] ?? "MyApi.Clients";

builder.Services.AddSingleton(new JwtTokenService(jwtKey, jwtIssuer, jwtAudience));
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(o =>
    {
        o.MapInboundClaims = false; // keep claim names like "sub" as-is
        o.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = jwtIssuer,
            ValidateAudience = true,
            ValidAudience = jwtAudience,
            ValidateIssuerSigningKey = true,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromMinutes(1)
        };
    });
builder.Services.AddAuthorization();

// --- CORS: allowed origins come from config (Cors:AllowedOrigins:0, :1, ...) ---
builder.Services.AddCors();
builder.Services.AddOptions<CorsOptions>().Configure<IConfiguration>((options, config) =>
{
    var origins = config.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];
    options.AddDefaultPolicy(p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod());
});

// --- Rate limiting (per client IP). Limits are read from config on each request ---
static RateLimitPartition<string> IpPartition(HttpContext ctx, string setting, int fallback) =>
    RateLimitPartition.GetFixedWindowLimiter(
        ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = ctx.RequestServices.GetRequiredService<IConfiguration>()
                             .GetValue<int?>(setting) ?? fallback,
            Window = TimeSpan.FromMinutes(1)
        });

builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    o.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
        IpPartition(ctx, "RateLimiting:GlobalPermitLimit", 100));
    o.AddPolicy("auth", ctx => IpPartition(ctx, "RateLimiting:AuthPermitLimit", 10));
});


builder.Services.Configure<ForwardedHeadersOptions>(o =>
{
    o.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
    // App Service's proxy addresses are not fixed, so trust the forwarded headers it sends
    o.KnownIPNetworks.Clear();
    o.KnownProxies.Clear();
});

var app = builder.Build();

app.UseForwardedHeaders();

app.Use(async (ctx, next) =>
{
    ctx.Response.Headers["X-Content-Type-Options"] = "nosniff";
    ctx.Response.Headers["X-Frame-Options"] = "DENY";
    ctx.Response.Headers["Referrer-Policy"] = "no-referrer";
    await next();
});
// Create/upgrade the database at startup (fine for a single-instance learning app)
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    if (IsSqlite(app.Configuration))
    {
        var csb = new SqliteConnectionStringBuilder(db.Database.GetConnectionString());
        if (!string.IsNullOrEmpty(csb.DataSource) && csb.DataSource != ":memory:")
        {
            Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(csb.DataSource))!);
        }
        db.Database.EnsureCreated();   // local dev and tests: no migrations needed
    }
    else
    {
        db.Database.Migrate();         // Azure SQL: apply real migrations
    }
}


app.UseCors();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();

var enableDocs = app.Environment.IsDevelopment()
                 || app.Configuration.GetValue<bool>("EnableApiDocs");

if (enableDocs)
{
    app.MapOpenApi();
    app.MapScalarApiReference();
}

app.MapGet("/", () => enableDocs ? Results.Redirect("/scalar") : Results.Ok("MyApi is running"))
   .ExcludeFromDescription();

app.MapHealthChecks("/health").ExcludeFromDescription();

static TimeResponse ToResponse(DateTimeOffset utcNow, TimeZoneInfo tz)
{
    var localNow = TimeZoneInfo.ConvertTime(utcNow, tz);
    return new TimeResponse(utcNow, localNow, tz.Id, tz.DisplayName, localNow.Offset.ToString());
}

// ---------- Time endpoints (public) ----------
app.MapGet("/api/time", (TimeProvider clock) =>
    Results.Ok(ToResponse(clock.GetUtcNow(), clock.LocalTimeZone)))
.WithName("GetCurrentTime")
.WithSummary("Gets the current server time and time zone")
.Produces<TimeResponse>(StatusCodes.Status200OK);

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
    return Results.Ok(ToResponse(clock.GetUtcNow(), tz));
})
.WithName("GetTimeByTimeZone")
.WithSummary("Gets the current time in the given time zone (e.g. Asia/Kolkata)")
.Produces<TimeResponse>(StatusCodes.Status200OK)
.Produces(StatusCodes.Status404NotFound);

// ---------- Auth endpoints (public) ----------
app.MapPost("/api/auth/register", async (RegisterRequest req, AppDbContext db, IPasswordHasher<User> hasher) =>
{
    if (string.IsNullOrWhiteSpace(req.Username) || req.Password is null || req.Password.Length < 8)
    {
        return Results.BadRequest(new { error = "Username is required and password must be at least 8 characters." });
    }

    var username = req.Username.Trim().ToLowerInvariant();
    if (await db.Users.AnyAsync(u => u.Username == username))
    {
        return Results.Conflict(new { error = "Username is already taken." });
    }

    var user = new User { Username = username };
    user.PasswordHash = hasher.HashPassword(user, req.Password);
    db.Users.Add(user);
    await db.SaveChangesAsync();

    return Results.Created($"/api/users/{user.Id}", new { user.Id, user.Username });
})
.WithTags("Auth")
.RequireRateLimiting("auth");

app.MapPost("/api/auth/login", async (LoginRequest req, AppDbContext db,
    IPasswordHasher<User> hasher, JwtTokenService tokens) =>
{
    var username = (req.Username ?? "").Trim().ToLowerInvariant();
    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == username);

    if (user is null ||
        hasher.VerifyHashedPassword(user, user.PasswordHash, req.Password ?? "") == PasswordVerificationResult.Failed)
    {
        return Results.Unauthorized();
    }

    var (token, expires) = tokens.Create(user);
    return Results.Ok(new LoginResponse(token, expires));
})
.WithTags("Auth")
.RequireRateLimiting("auth");

// ---------- Favorites (JWT required) ----------
var favorites = app.MapGroup("/api/favorites").RequireAuthorization().WithTags("Favorites");

favorites.MapGet("/", async (System.Security.Claims.ClaimsPrincipal principal, AppDbContext db, TimeProvider clock) =>
{
    var userId = principal.GetUserId();
    var items = await db.FavoriteZones.Where(f => f.UserId == userId).OrderBy(f => f.Id).ToListAsync();
    var utcNow = clock.GetUtcNow();

    return Results.Ok(items.Select(f => new FavoriteResponse(
        f.Id, f.TimeZoneId,
        TimeZoneInfo.ConvertTime(utcNow, TimeZoneInfo.FindSystemTimeZoneById(f.TimeZoneId)))));
});

favorites.MapPost("/", async (AddFavoriteRequest req, System.Security.Claims.ClaimsPrincipal principal,
    AppDbContext db, TimeProvider clock) =>
{
    if (string.IsNullOrWhiteSpace(req.TimeZoneId))
    {
        return Results.BadRequest(new { error = "timeZoneId is required." });
    }

    TimeZoneInfo tz;
    try
    {
        tz = TimeZoneInfo.FindSystemTimeZoneById(req.TimeZoneId);
    }
    catch (Exception ex) when (ex is TimeZoneNotFoundException or InvalidTimeZoneException)
    {
        return Results.BadRequest(new { error = $"Time zone '{req.TimeZoneId}' is not valid." });
    }

    var userId = principal.GetUserId();
    if (await db.FavoriteZones.AnyAsync(f => f.UserId == userId && f.TimeZoneId == tz.Id))
    {
        return Results.Conflict(new { error = "Already in your favorites." });
    }

    var fav = new FavoriteZone { UserId = userId, TimeZoneId = tz.Id, CreatedAtUtc = clock.GetUtcNow() };
    db.FavoriteZones.Add(fav);
    await db.SaveChangesAsync();

    return Results.Created($"/api/favorites/{fav.Id}", new FavoriteResponse(
        fav.Id, fav.TimeZoneId, TimeZoneInfo.ConvertTime(clock.GetUtcNow(), tz)));
});

favorites.MapDelete("/{id:int}", async (int id, System.Security.Claims.ClaimsPrincipal principal, AppDbContext db) =>
{
    var userId = principal.GetUserId();
    // Filtering by UserId means you can never delete someone else's data
    var fav = await db.FavoriteZones.FirstOrDefaultAsync(f => f.Id == id && f.UserId == userId);
    if (fav is null) return Results.NotFound();

    db.FavoriteZones.Remove(fav);
    await db.SaveChangesAsync();
    return Results.NoContent();
});

app.Run();

public record TimeResponse(
    DateTimeOffset UtcNow,
    DateTimeOffset LocalNow,
    string TimeZoneId,
    string TimeZoneName,
    string UtcOffset);

public record RegisterRequest(string Username, string Password);
public record LoginRequest(string Username, string Password);
public record LoginResponse(string Token, DateTimeOffset ExpiresAtUtc);
public record AddFavoriteRequest(string TimeZoneId);
public record FavoriteResponse(int Id, string TimeZoneId, DateTimeOffset LocalNow);

public partial class Program;