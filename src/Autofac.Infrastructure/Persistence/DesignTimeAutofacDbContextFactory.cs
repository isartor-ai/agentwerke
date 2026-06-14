using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Autofac.Infrastructure.Persistence;

public sealed class DesignTimeAutofacDbContextFactory : IDesignTimeDbContextFactory<AutofacDbContext>
{
    public AutofacDbContext CreateDbContext(string[] args)
    {
        // Allow the connection string to be overridden at migration time via an
        // environment variable (useful in Docker / CI).
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=autofac;Username=postgres;Password=postgres";

        var optionsBuilder = new DbContextOptionsBuilder<AutofacDbContext>();
        optionsBuilder.UseNpgsql(connectionString);
        return new AutofacDbContext(optionsBuilder.Options);
    }
}
