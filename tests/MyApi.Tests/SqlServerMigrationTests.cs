using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

using MyApi.Api.Data;

using Testcontainers.MsSql;

using Xunit;

namespace MyApi.Tests;

[Trait("Category", "SqlServer")]
public class SqlServerMigrationTests
{
    private sealed class SqlServerApiFactory(string connectionString) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder) =>
            builder.ConfigureAppConfiguration((_, config) =>
                config.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["Database:Provider"] = "SqlServer",
                    ["ConnectionStrings:Default"] = connectionString,
                    ["RateLimiting:AuthPermitLimit"] = "1000",
                    ["RateLimiting:GlobalPermitLimit"] = "10000"
                }));
    }

    [Fact]
    [Obsolete]
    public async Task Migrations_ApplyToRealSqlServer_AndApiWorksEndToEnd()
    {
        // 1. Start a throwaway SQL Server in Docker
        await using var sql = new MsSqlBuilder()
            .WithImage("mcr.microsoft.com/mssql/server:2022-latest")
            .Build();
        await sql.StartAsync();

        // 2. Boot the real app against it. Startup runs db.Database.Migrate()
        await using var factory = new SqlServerApiFactory(sql.GetConnectionString());
        var client = factory.CreateClient();

        // 3. Exercise the API end to end
        var username = $"user-{Guid.NewGuid():N}";
        const string password = "Passw0rd!123";

        var register = await client.PostAsJsonAsync("/api/auth/register", new { username, password });
        Assert.Equal(HttpStatusCode.Created, register.StatusCode);

        var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password });
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var token = (await login.Content.ReadFromJsonAsync<LoginResponse>())!.Token;
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);

        var add = await client.PostAsJsonAsync("/api/favorites", new { timeZoneId = "Asia/Kolkata" });
        Assert.Equal(HttpStatusCode.Created, add.StatusCode);

        var list = await client.GetFromJsonAsync<List<FavoriteResponse>>("/api/favorites");
        Assert.Single(list!);

        // 4. Confirm the migration history in the real database
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        Assert.NotEmpty(await db.Database.GetAppliedMigrationsAsync());
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
    }
}