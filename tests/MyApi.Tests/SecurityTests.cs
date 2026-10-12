using Microsoft.Extensions.Configuration;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace MyApi.Tests
{
    public class SecurityTests : IClassFixture<TimeApiFactory>
    {
        private readonly TimeApiFactory _factory;

        public SecurityTests(TimeApiFactory factory) => _factory = factory;

        [Fact]
        public async Task Login_TooManyAttempts_Returns429()
        {
            // A separate host with a tiny limit, so the other tests aren't affected
            var limited = _factory.WithWebHostBuilder(b =>
                b.ConfigureAppConfiguration((_, c) =>
                    c.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["RateLimiting:AuthPermitLimit"] = "3"
                    })));
            var client = limited.CreateClient();

            for (var i = 0; i < 3; i++)
            {
                var r = await client.PostAsJsonAsync("/api/auth/login", new { username = "nobody", password = "wrong-password" });
                Assert.Equal(HttpStatusCode.Unauthorized, r.StatusCode);
            }

            var blocked = await client.PostAsJsonAsync("/api/auth/login", new { username = "nobody", password = "wrong-password" });
            Assert.Equal(HttpStatusCode.TooManyRequests, blocked.StatusCode);
        }

        [Fact]
        public async Task Cors_AllowedOrigin_GetsHeader()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/time");
            request.Headers.Add("Origin", "https://app.example.com");

            var response = await _factory.CreateClient().SendAsync(request);

            Assert.True(response.Headers.TryGetValues("Access-Control-Allow-Origin", out var values));
            Assert.Equal("https://app.example.com", Assert.Single(values));
        }

        [Fact]
        public async Task Cors_UnknownOrigin_GetsNoHeader()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "/api/time");
            request.Headers.Add("Origin", "https://evil.example.net");

            var response = await _factory.CreateClient().SendAsync(request);

            Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
        }

        [Fact]
        public async Task Responses_HaveSecurityHeaders()
        {
            var response = await _factory.CreateClient().GetAsync("/health");

            Assert.Equal("nosniff", response.Headers.GetValues("X-Content-Type-Options").Single());
            Assert.Equal("DENY", response.Headers.GetValues("X-Frame-Options").Single());
        }
    }
}
