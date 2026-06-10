using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace Autofac.Infrastructure.Persistence;

public sealed class DesignTimeAutofacDbContextFactory : IDesignTimeDbContextFactory<AutofacDbContext>
{
    public AutofacDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<AutofacDbContext>();

        optionsBuilder.UseNpgsql(
            "Host=localhost;Port=5432;Database=autofac;Username=postgres;Password=postgres");

        return new AutofacDbContext(optionsBuilder.Options);
    }
}
