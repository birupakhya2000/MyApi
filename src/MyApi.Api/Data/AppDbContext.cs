using Microsoft.EntityFrameworkCore;

namespace MyApi.Api.Data
{
    public class User
    {
        public int Id { get; set; }
        public required string Username { get; set; }
        public string PasswordHash { get; set; } = "";
    }

    public class FavoriteZone
    {
        public int Id { get; set; }
        public int UserId { get; set; }
        public required string TimeZoneId { get; set; }
        public DateTimeOffset CreatedAtUtc { get; set; }
    }

    public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
    {
        public DbSet<User> Users => Set<User>();
        public DbSet<FavoriteZone> FavoriteZones => Set<FavoriteZone>();

        protected override void OnModelCreating(ModelBuilder b)
        {
            b.Entity<User>().HasIndex(u => u.Username).IsUnique();
            b.Entity<FavoriteZone>().HasIndex(f => new { f.UserId, f.TimeZoneId }).IsUnique();
        }
    }
}
