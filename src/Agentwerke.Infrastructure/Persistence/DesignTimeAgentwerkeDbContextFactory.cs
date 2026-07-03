using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
using Npgsql;

namespace Agentwerke.Infrastructure.Persistence;

public sealed class DesignTimeAgentwerkeDbContextFactory : IDesignTimeDbContextFactory<AgentwerkeDbContext>
{
    public AgentwerkeDbContext CreateDbContext(string[] args)
    {
        var connectionString =
            Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
            ?? "Host=localhost;Port=5432;Database=autofac;Username=postgres;Password=postgres";

        var dataSource = new NpgsqlDataSourceBuilder(connectionString)
            .EnableDynamicJson()
            .Build();

        var optionsBuilder = new DbContextOptionsBuilder<AgentwerkeDbContext>();
        optionsBuilder.UseNpgsql(dataSource);
        return new AgentwerkeDbContext(optionsBuilder.Options);
    }
}
