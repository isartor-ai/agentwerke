using Autofac.Domain.Persistence;
using Microsoft.EntityFrameworkCore;

namespace Autofac.Infrastructure.Persistence;

public sealed class AutofacDbContext(DbContextOptions<AutofacDbContext> options) : DbContext(options)
{
    public DbSet<WorkflowDefinition> WorkflowDefinitions => Set<WorkflowDefinition>();

    public DbSet<WorkflowRun> WorkflowRuns => Set<WorkflowRun>();

    public DbSet<WorkflowEvent> WorkflowEvents => Set<WorkflowEvent>();

    public DbSet<ApprovalRequest> ApprovalRequests => Set<ApprovalRequest>();

    public DbSet<PolicyDecision> PolicyDecisions => Set<PolicyDecision>();

    public DbSet<AgentSession> AgentSessions => Set<AgentSession>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasDefaultSchema("autofac");

        modelBuilder.Entity<WorkflowDefinition>(entity =>
        {
            entity.ToTable("workflow_definitions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.WorkflowKey).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Name).HasMaxLength(256).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(64).IsRequired();
            entity.HasIndex(e => new { e.WorkflowKey, e.Version }).IsUnique();
        });

        modelBuilder.Entity<WorkflowRun>(entity =>
        {
            entity.ToTable("workflow_runs");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Status).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Initiator).HasMaxLength(128);
            entity.HasOne(e => e.WorkflowDefinition)
                .WithMany(e => e.Runs)
                .HasForeignKey(e => e.WorkflowDefinitionId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.WorkflowDefinitionId);
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<WorkflowEvent>(entity =>
        {
            entity.ToTable("workflow_events");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.EventType).HasMaxLength(128).IsRequired();
            entity.Property(e => e.PayloadJson).HasColumnType("jsonb").IsRequired();
            entity.HasOne(e => e.WorkflowRun)
                .WithMany(e => e.Events)
                .HasForeignKey(e => e.WorkflowRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.WorkflowRunId);
            entity.HasIndex(e => e.CreatedAtUtc);
        });

        modelBuilder.Entity<ApprovalRequest>(entity =>
        {
            entity.ToTable("approval_requests");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.ApprovalType).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(64).IsRequired();
            entity.Property(e => e.RequestedBy).HasMaxLength(128).IsRequired();
            entity.HasOne(e => e.WorkflowRun)
                .WithMany(e => e.Approvals)
                .HasForeignKey(e => e.WorkflowRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.WorkflowRunId);
            entity.HasIndex(e => e.Status);
        });

        modelBuilder.Entity<PolicyDecision>(entity =>
        {
            entity.ToTable("policy_decisions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.PolicyName).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Decision).HasMaxLength(64).IsRequired();
            entity.Property(e => e.Reason).HasMaxLength(2048).IsRequired();
            entity.Property(e => e.EvidenceJson).HasColumnType("jsonb").IsRequired();
            entity.HasOne(e => e.WorkflowRun)
                .WithMany(e => e.PolicyDecisions)
                .HasForeignKey(e => e.WorkflowRunId)
                .OnDelete(DeleteBehavior.SetNull);
            entity.HasIndex(e => e.WorkflowRunId);
            entity.HasIndex(e => e.EvaluatedAtUtc);
        });

        modelBuilder.Entity<AgentSession>(entity =>
        {
            entity.ToTable("agent_sessions");
            entity.HasKey(e => e.Id);
            entity.Property(e => e.AgentName).HasMaxLength(128).IsRequired();
            entity.Property(e => e.Status).HasMaxLength(64).IsRequired();
            entity.HasOne(e => e.WorkflowRun)
                .WithMany(e => e.AgentSessions)
                .HasForeignKey(e => e.WorkflowRunId)
                .OnDelete(DeleteBehavior.Cascade);
            entity.HasIndex(e => e.WorkflowRunId);
            entity.HasIndex(e => new { e.AgentName, e.Status });
        });
    }
}
