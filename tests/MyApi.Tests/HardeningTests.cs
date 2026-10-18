using System.Net;
using System.Net.Http.Json;

using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

using Xunit;

namespace MyApi.Tests;

public class HardeningTests : IClassFixture<TimeApiFactory>
{
    private readonly TimeApiFactory _factory;

    public HardeningTests(TimeApiFactory factory) => _factory = factory;

    private sealed class ThrowingTimeProvider : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() =>
            throw new InvalidOperationException("secret-internal-detail");
    }

    [Theory]
    [InlineData("ab", "Passw0rd!123", "username")]
    [InlineData("bad user!", "Passw0rd!123", "username")]
    [InlineData("valid-user", "short", "password")]
    public async Task Register_InvalidInput_ReturnsValidationProblem(string username, string password, string field)
    {
        var response = await _factory.CreateClient()
            .PostAsJsonAsync("/api/auth/register", new { username, password });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var body = await response.Content.ReadAsStringAsync();
        Assert.Contains("\"errors\"", body);
        Assert.Contains($"\"{field}\"", body);
    }

    [Fact]
    public async Task Register_HugePassword_IsRejected()
    {
        var response = await _factory.CreateClient().PostAsJsonAsync("/api/auth/register",
            new { username = "valid-user", password = new string('a', 5000) });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Ready_ChecksDatabase_ReturnsHealthy()
    {
        var response = await _factory.CreateClient().GetAsync("/health/ready");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task UnhandledException_ReturnsProblemDetails_WithoutLeakingDetails()
    {
        // Program.cs reads Jwt:Key before test config overrides apply, and
        // appsettings.Development.json isn't loaded in Production, so supply the key
        // through an environment variable, which is read at startup.
        const string keyVariable = "Jwt__Key";
        Environment.SetEnvironmentVariable(keyVariable, "test-only-key-test-only-key-test-only-key-1234");

        try
        {
            // Production mode, so nothing developer-friendly can leak
            var factory = _factory.WithWebHostBuilder(builder =>
            {
                builder.UseEnvironment("Production");
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(new ThrowingTimeProvider());
                });
            });

            var response = await factory.CreateClient().GetAsync("/api/time");

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
            Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
            Assert.DoesNotContain("secret-internal-detail", await response.Content.ReadAsStringAsync());
            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
        }
        finally
        {
            Environment.SetEnvironmentVariable(keyVariable, null);
        }
    }
}