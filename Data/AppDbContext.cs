using IslandParrotCourier.Data.Entities;
using Microsoft.EntityFrameworkCore;

namespace IslandParrotCourier.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Game> Games { get; set; }
    public DbSet<Player> Players { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Game>(entity =>
        {
            entity.HasKey(g => g.Id);
            entity.Property(g => g.Name).IsRequired();
            entity.Property(g => g.Host).IsRequired();
            entity.HasIndex(g => g.Name).IsUnique();
        });

        modelBuilder.Entity<Player>(entity =>
        {
            entity.HasKey(p => p.Id);
            entity.Property(p => p.SlotName).IsRequired();
            entity.HasOne(p => p.Game)
                  .WithMany(g => g.Players)
                  .HasForeignKey(p => p.GameId)
                  .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(p => new { p.GameId, p.SlotName }).IsUnique();
            entity.HasIndex(p => new { p.GameId, p.DiscordUserId }).IsUnique();
        });
    }
}
