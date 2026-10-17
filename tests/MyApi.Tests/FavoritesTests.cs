using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;

namespace MyApi.Tests
{
    public class FavoritesTests : IClassFixture<TimeApiFactory>
    {
        private const string Password = "Passw0rd!123";
        private readonly TimeApiFactory _factory;

        public FavoritesTests(TimeApiFactory factory) => _factory = factory;

        private async Task<HttpClient> CreateLoggedInClientAsync()
        {
            var client = _factory.CreateClient();
            var username = $"user-{Guid.NewGuid():N}";

            var reg = await client.PostAsJsonAsync("/api/auth/register", new { username, password = Password });
            Assert.Equal(HttpStatusCode.Created, reg.StatusCode);

            var login = await client.PostAsJsonAsync("/api/auth/login", new { username, password = Password });
            Assert.Equal(HttpStatusCode.OK, login.StatusCode);

            var body = await login.Content.ReadFromJsonAsync<LoginResponse>();
            client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", body!.Token);
            return client;
        }

        [Fact]
        public async Task Favorites_WithoutToken_Returns401()
        {
            var response = await _factory.CreateClient().GetAsync("/api/favorites");
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Login_WrongPassword_Returns401()
        {
            var client = _factory.CreateClient();
            var username = $"user-{Guid.NewGuid():N}";
            await client.PostAsJsonAsync("/api/auth/register", new { username, password = Password });

            var response = await client.PostAsJsonAsync("/api/auth/login", new { username, password = "wrong-password" });
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }

        [Fact]
        public async Task Register_DuplicateUsername_Returns409()
        {
            var client = _factory.CreateClient();
            var username = $"user-{Guid.NewGuid():N}";

            await client.PostAsJsonAsync("/api/auth/register", new { username, password = Password });
            var second = await client.PostAsJsonAsync("/api/auth/register", new { username, password = Password });

            Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        }

        [Fact]
        public async Task AddAndListFavorite_Works()
        {
            var client = await CreateLoggedInClientAsync();

            var add = await client.PostAsJsonAsync("/api/favorites", new { timeZoneId = "Asia/Kolkata" });
            Assert.Equal(HttpStatusCode.Created, add.StatusCode);

            var list = await client.GetFromJsonAsync<List<FavoriteResponse>>("/api/favorites");
            var item = Assert.Single(list!);
            Assert.Equal("Asia/Kolkata", item.TimeZoneId);
            Assert.Equal(15, item.LocalNow.Hour); // 10:00 UTC + 5:30
        }

        [Fact]
        public async Task AddFavorite_UnknownZone_Returns400()
        {
            var client = await CreateLoggedInClientAsync();

            var response = await client.PostAsJsonAsync("/api/favorites", new { timeZoneId = "Mars/Olympus" });
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        [Fact]
        public async Task Users_CannotSeeEachOthersFavorites()
        {
            var alice = await CreateLoggedInClientAsync();
            var bob = await CreateLoggedInClientAsync();

            await alice.PostAsJsonAsync("/api/favorites", new { timeZoneId = "Asia/Tokyo" });

            var bobsList = await bob.GetFromJsonAsync<List<FavoriteResponse>>("/api/favorites");
            Assert.Empty(bobsList!);
        }
    }
}