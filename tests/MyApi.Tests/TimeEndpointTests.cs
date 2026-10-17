using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Json;
using System.Text;

namespace MyApi.Tests
{
    public class TimeEndpointTests : IClassFixture<TimeApiFactory>
    {
        private readonly HttpClient _client;
        public TimeEndpointTests(TimeApiFactory factory)
        {
            _client = factory.CreateClient();
        }

        [Fact]
        public async Task GetCurrentTime_ReturnsOk_WithFixedUtcTime()
        {
            var response = await _client.GetAsync("/api/time");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TimeResponse>();
            Assert.NotNull(body);
            Assert.Equal(TimeApiFactory.FixedUtcNow, body.UtcNow);
            Assert.False(string.IsNullOrWhiteSpace(body.TimeZoneId));
        }

        [Fact]
        public async Task GetTimeByTimeZone_Kolkata_ReturnsIst()
        {
            var response = await _client.GetAsync("/api/time/Asia/Kolkata");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TimeResponse>();
            Assert.NotNull(body);
            Assert.Equal("Asia/Kolkata", body.TimeZoneId);
            Assert.Equal(TimeSpan.FromHours(5.5), body.LocalNow.Offset);
            Assert.Equal(15, body.LocalNow.Hour);   // 10:00 UTC + 5:30
            Assert.Equal(30, body.LocalNow.Minute);
        }

        [Theory]
        [InlineData("Asia/Tokyo", 9)]
        [InlineData("America/New_York", -5)]   // January = standard time
        [InlineData("UTC", 0)]
        public async Task GetTimeByTimeZone_ReturnsCorrectOffset(string zone, int expectedHours)
        {
            var response = await _client.GetAsync($"/api/time/{zone}");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var body = await response.Content.ReadFromJsonAsync<TimeResponse>();
            Assert.NotNull(body);
            Assert.Equal(TimeSpan.FromHours(expectedHours), body.LocalNow.Offset);
            Assert.Equal(TimeApiFactory.FixedUtcNow, body.UtcNow);
        }

        [Fact]
        public async Task GetTimeByTimeZone_UnknownZone_ReturnsNotFound()
        {
            var response = await _client.GetAsync("/api/time/Mars/Olympus");

            Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        }

        [Fact]
        public async Task OpenApiDocument_IsServed()
        {
            var response = await _client.GetAsync("/openapi/v1.json");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);

            var json = await response.Content.ReadAsStringAsync();
            Assert.Contains("/api/time", json);
        }

        [Fact]
        public async Task Health_ReturnsHealthy()
        {
            var response = await _client.GetAsync("/health");

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Equal("Healthy", await response.Content.ReadAsStringAsync());
        }
    }
}