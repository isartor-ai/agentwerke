using Agentwerke.Domain.Persistence;
using Agentwerke.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata;

namespace Agentwerke.Api.Tests.Persistence;

public sealed class AgentInteractionPersistenceModelTests
{
    [Fact]
    public void AgentInteraction_MapsRoutingExpiryAndConcurrencyMetadata()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(AgentInteraction));

        Assert.NotNull(entity);
        Assert.Equal(32, entity!.FindProperty(nameof(AgentInteraction.RespondedChannel))!.GetMaxLength());
        Assert.Equal(64, entity.FindProperty(nameof(AgentInteraction.TimeoutAt))!.GetMaxLength());
        Assert.Equal(32, entity.FindProperty(nameof(AgentInteraction.ExpiresAction))!.GetMaxLength());
        Assert.Equal(8192, entity.FindProperty(nameof(AgentInteraction.DefaultAnswer))!.GetMaxLength());
        Assert.Equal(64, entity.FindProperty(nameof(AgentInteraction.CancelledAt))!.GetMaxLength());
        Assert.Equal(128, entity.FindProperty(nameof(AgentInteraction.CancelledBy))!.GetMaxLength());
        Assert.Equal(64, entity.FindProperty(nameof(AgentInteraction.ResumedAt))!.GetMaxLength());

        var requestedChannels = entity.FindProperty(nameof(AgentInteraction.RequestedChannels));
        Assert.Equal("jsonb", requestedChannels!.GetColumnType());
        Assert.NotNull(requestedChannels!.GetValueComparer());
        Assert.Equal("'[]'::jsonb", requestedChannels.GetDefaultValueSql());

        var version = entity.FindProperty(nameof(AgentInteraction.Version));
        Assert.True(version!.IsConcurrencyToken);
        Assert.Equal(0, version.GetDefaultValue());

        AssertIndex(entity, false, nameof(AgentInteraction.RunId));
        AssertIndex(entity, false, nameof(AgentInteraction.Status));
        AssertIndex(entity, false, nameof(AgentInteraction.Status), nameof(AgentInteraction.TimeoutAt));
        AssertIndex(entity, false, nameof(AgentInteraction.CorrelationId));
    }

    [Fact]
    public void InteractionDelivery_MapsTableColumnsAndIdempotencyIndexes()
    {
        using var context = CreateContext();
        var entity = context.Model.FindEntityType(typeof(InteractionDelivery));

        Assert.NotNull(entity);
        Assert.Equal("interaction_deliveries", entity!.GetTableName());
        Assert.Equal(64, entity.FindProperty(nameof(InteractionDelivery.InteractionId))!.GetMaxLength());
        Assert.Equal(32, entity.FindProperty(nameof(InteractionDelivery.Channel))!.GetMaxLength());
        Assert.Equal(32, entity.FindProperty(nameof(InteractionDelivery.Status))!.GetMaxLength());
        Assert.Equal(256, entity.FindProperty(nameof(InteractionDelivery.ChannelMessageId))!.GetMaxLength());
        Assert.Equal(1024, entity.FindProperty(nameof(InteractionDelivery.LastError))!.GetMaxLength());
        Assert.Equal(64, entity.FindProperty(nameof(InteractionDelivery.CreatedAt))!.GetMaxLength());
        Assert.Equal(64, entity.FindProperty(nameof(InteractionDelivery.LastAttemptAt))!.GetMaxLength());

        AssertIndex(entity, false, nameof(InteractionDelivery.InteractionId));
        AssertIndex(entity, true, nameof(InteractionDelivery.InteractionId), nameof(InteractionDelivery.Channel));
        AssertIndex(entity, false, nameof(InteractionDelivery.Channel), nameof(InteractionDelivery.ChannelMessageId));
        Assert.NotNull(context.InteractionDeliveries);
    }

    private static AgentwerkeDbContext CreateContext()
    {
        var options = new DbContextOptionsBuilder<AgentwerkeDbContext>()
            .UseNpgsql("Host=localhost;Database=agentwerke;Username=postgres;Password=postgres")
            .Options;

        return new AgentwerkeDbContext(options);
    }

    private static void AssertIndex(IEntityType entity, bool unique, params string[] propertyNames)
    {
        var index = entity.GetIndexes().SingleOrDefault(candidate =>
            candidate.Properties.Select(property => property.Name).SequenceEqual(propertyNames));

        Assert.NotNull(index);
        Assert.Equal(unique, index!.IsUnique);
    }
}
