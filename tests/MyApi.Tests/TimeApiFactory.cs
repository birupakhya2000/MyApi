using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Time.Testing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using System;
using System.Collections.Generic;
using System.Text;

namespace MyApi.Tests
{
    public class TimeApiFactory : WebApplicationFactory<Program>
    {
        public static readonly DateTimeOffset FixedUtcNow = new(2026, 1, 15, 10, 0, 0, TimeSpan.Zero);

        protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(new FakeTimeProvider(FixedUtcNow));
            });
        }
    }
}
