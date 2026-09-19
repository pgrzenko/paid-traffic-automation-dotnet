using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace PaidTraffic.Api.Persistence;

public sealed class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TrafficDbContext>
{
    public TrafficDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<TrafficDbContext>()
        .UseNpgsql(Environment.GetEnvironmentVariable("ConnectionStrings__Traffic") ?? "Host=localhost;Database=traffic;Username=traffic")
        .Options);
}
