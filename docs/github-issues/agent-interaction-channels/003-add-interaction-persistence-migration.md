# Map the new interaction fields and add the migration

**Parent:** [001](001-agent-clarification-confirmation-collaboration.md)
**Depends on:** 002.
**Blocks:** 004.
**Layer:** backend / migration.

## Context

`agent_interactions` is mapped in `AgentwerkeDbContext.cs:160` with `HasIndex(RunId)` and `Options`
stored as `jsonb` via `SerializeStringList`/`DeserializeStringList`. Two migrations already exist:
`20260702132231_AddAgentInteractions` and `20260713070316_AddToolAccessContext`. This table has live
rows, so the migration must be additive.

## Objective

Persist the 002 fields, add the concurrency token and the indexes the router and sweeper query on.

## Implementation steps

1. **Map the new `AgentInteraction` columns** in `OnModelCreating` alongside the existing block:
   ```csharp
   entity.Property(e => e.RespondedChannel).HasMaxLength(32);
   entity.Property(e => e.TimeoutAt).HasMaxLength(64);
   entity.Property(e => e.ExpiresAction).HasMaxLength(32);
   entity.Property(e => e.DefaultAnswer).HasMaxLength(8192);
   entity.Property(e => e.CancelledAt).HasMaxLength(64);
   entity.Property(e => e.CancelledBy).HasMaxLength(128);
   entity.Property(e => e.ResumedAt).HasMaxLength(64);
   entity.Property(e => e.RequestedChannels)
       .HasConversion(list => SerializeStringList(list), json => DeserializeStringList(json))
       .HasColumnType("jsonb")
       .Metadata.SetValueComparer(StringListComparer);
   ```
   Reuse the existing converter and `StringListComparer` — do not write a second one.

2. **Configure the concurrency token:**
   ```csharp
   entity.Property(e => e.Version).IsConcurrencyToken().HasDefaultValue(0);
   ```
   **Use an `int` token, not Postgres `xmin`.** Every unit-test fake in this repo is hand-rolled
   against the repository interfaces (`tests/Agentwerke.Agents.Tests/InMemoryInteractionRepository.cs`
   is a plain `List<AgentInteraction>`; `WorkflowRunOrchestrationServiceTests` does the same) — there
   is no EF InMemory provider, no SQLite, and no Testcontainers in `tests/`. `xmin` is an EF shadow
   property no hand-rolled fake can emulate, so an `xmin` token would push the 004 race test into the
   Docker E2E stack only. An `int Version` is an ordinary property the fakes implement directly.

3. **Add indexes:**
   ```csharp
   entity.HasIndex(e => e.Status);
   entity.HasIndex(e => new { e.Status, e.TimeoutAt });   // sweeper query (007)
   entity.HasIndex(e => e.CorrelationId);                 // agent request/reply pairing (009)
   ```
   Keep the existing `HasIndex(e => e.RunId)`.

4. **Map `InteractionDelivery`** to table `interaction_deliveries`: key `Id`; `InteractionId`
   `HasMaxLength(64).IsRequired()`; `Channel` 32; `Status` 32; `ChannelMessageId` 256; `LastError`
   1024; `CreatedAt`/`LastAttemptAt` 64. Add `HasIndex(InteractionId)`, a **unique**
   `HasIndex(InteractionId, Channel)` (one delivery row per channel per interaction — the router's
   idempotency anchor), and `HasIndex(Channel, ChannelMessageId)` for inbound correlation.

5. **Add `DbSet<InteractionDelivery> InteractionDeliveries`** next to the existing `AgentInteractions`
   set.

6. **Generate the migration:**
   ```bash
   dotnet ef migrations add AddInteractionChannelsAndDeliveries \
     --project src/Agentwerke.Infrastructure --startup-project src/Agentwerke.Api
   ```
   Review the generated `Up()`: it must be **additive only** — `AddColumn` (nullable, or defaulted for
   `Version`/`DelegationDepth`), `CreateTable`, `CreateIndex`. No `AlterColumn` on existing columns, no
   data migration, no backfill. Confirm `Down()` drops cleanly.

7. **Verify against populated data.** Existing rows must read back valid: no channels requested, no
   timeout, `Version = 0`, `DelegationDepth = 0`.

## Files

- `src/Agentwerke.Infrastructure/Persistence/AgentwerkeDbContext.cs` (line ~160 and the DbSet block)
- `src/Agentwerke.Infrastructure/Persistence/Migrations/` (new migration + designer + snapshot)

## Acceptance criteria

- Migration applies to a database populated with pre-existing `agent_interactions` rows, and reverses.
- Existing rows load without error and are unchanged in meaning.
- `Version` increments are rejected on stale writes (proved in 004).
- The unique `(InteractionId, Channel)` index is present.

## Verification

```bash
dotnet build Agentwerke.sln
docker compose -f docker/docker-compose.e2e.yml up -d postgres
dotnet ef database update --project src/Agentwerke.Infrastructure --startup-project src/Agentwerke.Api
dotnet ef migrations remove --project src/Agentwerke.Infrastructure --startup-project src/Agentwerke.Api  # reversibility check
```

## Out of scope

Repository methods (004). Reading any new column.
