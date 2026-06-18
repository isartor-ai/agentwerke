using System.Text.Json;
using Autofac.Domain.AgentRuntime;
using Autofac.Domain.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;

namespace Autofac.Infrastructure.Persistence;

public sealed class AutofacDbContext(DbContextOptions<AutofacDbContext> options) : DbContext(options)
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static readonly ValueComparer<List<string>> StringListComparer = new(
        (a, b) => a != null && b != null && a.SequenceEqual(b),
        c => c.Aggregate(0, (h, v) => HashCode.Combine(h, v.GetHashCode(StringComparison.Ordinal))),
        c => c.ToList());

    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();

    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();

    public DbSet<WorkflowRunStep> WorkflowRunSteps => Set<WorkflowRunStep>();

    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();

    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    public DbSet<AuditRecord> AuditRecords => Set<AuditRecord>();

    public DbSet<OutboxEntry> OutboxEntries => Set<OutboxEntry>();

    public DbSet<RunContextEntry> RunContextEntries => Set<RunContextEntry>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("autofac");

        modelBuilder.Entity<WorkflowDefinition>(entity =>
        {
            entity.ToTable("workflows");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Description).HasMaxLength(1024);
            entity.Property(e => e.Version).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Owner).HasMaxLength(128);
            entity.Property(e => e.ValidationState).HasMaxLength(64);
            entity.Property(e => e.Tags)
                .HasConversion(
                    list => SerializeStringList(list),
                    json => DeserializeStringList(json))
                .HasColumnType("jsonb")
                .Metadata.SetValueComparer(StringListComparer);
            entity.Property(e => e.BpmnXml).IsRequired();
        });

        modelBuilder.Entity<WorkflowRun>(entity =>
        {
            entity.ToTable("workflow_runs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.WorkflowId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.WorkflowName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.WorkflowVersion).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(64).IsRequired();
            entity.Property(e => e.RiskLevel).HasMaxLength(64);
            entity.Property(e => e.CurrentStep).HasMaxLength(256);
            entity.Property(e => e.RequestedBy).HasMaxLength(128);
            entity.Property(e => e.Tags)
                .HasConversion(
                    list => SerializeStringList(list),
                    json => DeserializeStringList(json))
                .HasColumnType("jsonb")
                .Metadata.SetValueComparer(StringListComparer);
            entity.Property(e => e.CorrelationId).HasMaxLength(128);
            entity.HasMany(e => e.Steps).WithOne().HasForeignKey("RunId");
            entity.HasMany(e => e.Events).WithOne().HasForeignKey(e => e.RunId);
        });

        modelBuilder.Entity<WorkflowRunStep>(entity =>
        {
            entity.ToTable("workflow_run_steps");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Type).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(64).IsRequired();
            entity.Property(e => e.AgentName).HasMaxLength(128);
            entity.Property(e => e.Error).HasColumnType("text");
            entity.Property(e => e.RuntimeSnapshot)
                .HasConversion(
                    snapshot => SerializeRuntimeSnapshot(snapshot),
                    json => DeserializeRuntimeSnapshot(json))
                .HasColumnType("jsonb");
            entity.OwnsOne(e => e.PolicyDecision, pd =>
            {
                pd.Property(p => p.Kind).HasMaxLength(64);
                pd.Property(p => p.PolicyId).HasMaxLength(128);
                pd.Property(p => p.PolicyName).HasMaxLength(256);
                pd.Property(p => p.Rationale).HasMaxLength(1024);
                pd.Property(p => p.RiskLevel).HasMaxLength(64);
                pd.Property(p => p.RiskFactors)
                    .HasConversion(
                    list => SerializeStringList(list),
                    json => DeserializeStringList(json))
                    .HasColumnType("jsonb")
                    .Metadata.SetValueComparer(StringListComparer);
                pd.Property(p => p.Constraints)
                    .HasConversion(
                    list => SerializeStringList(list),
                    json => DeserializeStringList(json))
                    .HasColumnType("jsonb")
                    .Metadata.SetValueComparer(StringListComparer);
            });
        });

        modelBuilder.Entity<WorkflowEvent>(entity =>
        {
            entity.ToTable("workflow_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RunId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Type).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Message).HasMaxLength(2048);
        });

        modelBuilder.Entity<ApprovalRequest>(entity =>
        {
            entity.ToTable("approval_requests");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RunId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.WorkflowName).HasMaxLength(256).IsRequired();
            entity.Property(e => e.ActionRequested).HasMaxLength(1024).IsRequired();
            entity.Property(e => e.Requester).HasMaxLength(128).IsRequired();
            entity.Property(e => e.AgentName).HasMaxLength(128);
            entity.Property(e => e.PolicyRationale).HasMaxLength(2048);
            entity.Property(e => e.RiskLevel).HasMaxLength(64);
            entity.Property(e => e.RiskFactors)
                .HasConversion(
                    list => SerializeStringList(list),
                    json => DeserializeStringList(json))
                .HasColumnType("jsonb")
                .Metadata.SetValueComparer(StringListComparer);
            entity.Property(e => e.AffectedSystems)
                .HasConversion(
                    list => SerializeStringList(list),
                    json => DeserializeStringList(json))
                .HasColumnType("jsonb")
                .Metadata.SetValueComparer(StringListComparer);
            entity.Property(e => e.Status).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Priority).HasMaxLength(64);
            entity.Property(e => e.DecisionComment).HasMaxLength(2048);
            entity.Property(e => e.DecidedBy).HasMaxLength(128);
        });

        modelBuilder.Entity<OutboxEntry>(entity =>
        {
            entity.ToTable("run_outbox");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Operation).HasMaxLength(64).IsRequired();
            entity.Property(e => e.RunId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Payload).HasColumnType("text");
            entity.Property(e => e.LockedBy).HasMaxLength(128);
            entity.Property(e => e.Error).HasColumnType("text");
            entity.HasIndex(e => new { e.LockedBy, e.CompletedAt, e.VisibleAfter })
                .HasDatabaseName("ix_run_outbox_claim");
        });

        modelBuilder.Entity<RunContextEntry>(entity =>
        {
            entity.ToTable("workflow_run_context");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RunId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Key).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Value).HasColumnType("text");
            entity.Property(e => e.Kind).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => new { e.RunId, e.Key })
                .IsUnique()
                .HasDatabaseName("ix_workflow_run_context_run_key");
        });

        modelBuilder.Entity<AuditRecord>(entity =>
        {
            entity.ToTable("audit_records");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RunId).HasMaxLength(128).IsRequired();
            entity.Property(e => e.CorrelationId).HasMaxLength(128);
            entity.Property(e => e.ActorType).HasMaxLength(32).IsRequired();
            entity.Property(e => e.Actor).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Action).HasMaxLength(256).IsRequired();
            entity.Property(e => e.ResourceType).HasMaxLength(128);
            entity.Property(e => e.ResourceId).HasMaxLength(256);
            entity.Property(e => e.Outcome).HasMaxLength(64).IsRequired();
        });
    }

    private static string SerializeStringList(List<string> list) =>
        JsonSerializer.Serialize(list, JsonOptions);

    private static List<string> DeserializeStringList(string json) =>
        JsonSerializer.Deserialize<List<string>>(json, JsonOptions) ?? [];

    private static string? SerializeRuntimeSnapshot(AgentRuntimeSnapshot? snapshot) =>
        snapshot is null ? null : JsonSerializer.Serialize(snapshot, JsonOptions);

    private static AgentRuntimeSnapshot? DeserializeRuntimeSnapshot(string? json) =>
        string.IsNullOrWhiteSpace(json)
            ? null
            : JsonSerializer.Deserialize<AgentRuntimeSnapshot>(json, JsonOptions);
}
