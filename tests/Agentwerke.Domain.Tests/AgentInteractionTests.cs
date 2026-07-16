using Agentwerke.Domain.Persistence;

namespace Agentwerke.Domain.Tests;

public sealed class AgentInteractionTests
{
    [Theory]
    [InlineData(AgentInteractionStatuses.Answered)]
    [InlineData(AgentInteractionStatuses.Rejected)]
    [InlineData(AgentInteractionStatuses.Expired)]
    [InlineData(AgentInteractionStatuses.Cancelled)]
    [InlineData(AgentInteractionStatuses.Posted)]
    public void IsTerminal_ReturnsTrueForTerminalStatuses(string status)
    {
        Assert.True(AgentInteractionStatuses.IsTerminal(status));
    }

    [Fact]
    public void IsTerminal_ReturnsFalseForPendingStatus()
    {
        Assert.False(AgentInteractionStatuses.IsTerminal(AgentInteractionStatuses.Pending));
    }

    [Fact]
    public void AgentInteraction_DefaultsNewCollectionAndConcurrencyFields()
    {
        var interaction = new AgentInteraction();

        Assert.Empty(interaction.RequestedChannels);
        Assert.Equal(0, interaction.DelegationDepth);
        Assert.Equal(0, interaction.Version);
    }

    [Fact]
    public void InteractionDelivery_DefaultsToPendingAndZeroAttempts()
    {
        var delivery = new InteractionDelivery();

        Assert.Equal(InteractionDeliveryStatuses.Pending, delivery.Status);
        Assert.Equal(0, delivery.Attempts);
    }
}
