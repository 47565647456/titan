using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Titan.API.Data;

/// <summary>
/// Design-time DbContext factory for EF Core migrations.
/// Used by `dotnet ef migrations` commands.
/// </summary>
public class AdminDbContextFactory : IDesignTimeDbContextFactory<AdminDbContext>
{
    public AdminDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AdminDbContext>();
        
        // Use a dummy connection string for design-time migrations
        // The actual connection string is provided at runtime by Aspire
        optionsBuilder.UseNpgsql("Host=localhost;Database=titan-admin;Username=postgres;Password=migration-design-time");
        
        return new AdminDbContext(optionsBuilder.Options);
    }
}
