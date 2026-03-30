namespace SmartTripPlanner.Api.Data;

using SmartTripPlanner.Api.Models;
using Microsoft.EntityFrameworkCore;

public class SmartTripPlannerDbContext(DbContextOptions<SmartTripPlannerDbContext> options) : DbContext(options)
{
    public DbSet<Trip> Trips => Set<Trip>();
    public DbSet<TripEvent> TripEvents => Set<TripEvent>();
    public DbSet<CachedLocation> CachedLocations => Set<CachedLocation>();
    public DbSet<UserPreference> UserPreferences => Set<UserPreference>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Trip>()
            .HasMany(t => t.Events)
            .WithOne(e => e.Trip)
            .HasForeignKey(e => e.TripId);

        modelBuilder.Entity<UserPreference>()
            .HasIndex(p => p.Key)
            .IsUnique();

        modelBuilder.Entity<UserPreference>()
            .Property(p => p.Source)
            .HasDefaultValue("user");

        modelBuilder.Entity<CachedLocation>()
            .HasOne(cl => cl.Trip)
            .WithMany()
            .HasForeignKey(cl => cl.TripId)
            .OnDelete(DeleteBehavior.SetNull);
    }
}
