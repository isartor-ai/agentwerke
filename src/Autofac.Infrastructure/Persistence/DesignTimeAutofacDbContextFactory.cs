using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Autofac.Infrastructure.Persistence;

public sealed class DesignTimeAutofacDbContextFactory : IDesignTimeDbContextFactory<AutofacDbContext>
{
    public AutofacDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=autofac;Username=postgres;Password=postgres";

        var dataSource = new NpgsqlDataSourceBuilder(connectionString)
            .EnableDynamicJson()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<AutofacDbContext>();
        optionsBuilder.UseNpgsql(dataSource);
        return new AutofacDbContext(optionsBuilder.Options);
    }
}
