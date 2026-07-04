using Agentwerke.Api.Auth;
using Agentwerke.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;

namespace Agentwerke.Api.Tests;

public sealed class AuthorizationMetadataTests
{
    [Theory]
    [InlineData(typeof(AgentsController))]
    [InlineData(typeof(SkillsController))]
    [InlineData(typeof(WorkflowsController))]
    [InlineData(typeof(RunsController))]
    [InlineData(typeof(ApprovalsController))]
    [InlineData(typeof(TemplatesController))]
    public void ProductReadControllers_RequireViewerPolicy(Type controllerType)
    {
        var attribute = Assert.Single(controllerType.GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(AgentwerkePolicies.Viewer, attribute.Policy);
    }

    [Theory]
    [InlineData(typeof(HealthController))]
    [InlineData(typeof(WebhooksController))]
    public void InfrastructureControllers_AllowAnonymous(Type controllerType)
    {
        Assert.NotEmpty(controllerType.GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(nameof(AuthController.GetAuthConfig))]
    [InlineData(nameof(AuthController.IssueDevToken))]
    public void AuthDiscoveryActions_AllowAnonymous(string actionName)
    {
        var method = typeof(AuthController).GetMethods()
            .Single(method => method.Name == actionName);

        Assert.NotEmpty(method.GetCustomAttributes<AllowAnonymousAttribute>());
    }

    [Theory]
    [InlineData(typeof(AgentsController), nameof(AgentsController.Upsert), AgentwerkePolicies.Admin)]
    [InlineData(typeof(AgentsController), nameof(AgentsController.Upload), AgentwerkePolicies.Admin)]
    [InlineData(typeof(WorkflowsController), nameof(WorkflowsController.Import), AgentwerkePolicies.Operator)]
    [InlineData(typeof(WorkflowsController), nameof(WorkflowsController.Validate), AgentwerkePolicies.Operator)]
    [InlineData(typeof(WorkflowsController), nameof(WorkflowsController.PolicySimulation), AgentwerkePolicies.Operator)]
    [InlineData(typeof(WorkflowsController), nameof(WorkflowsController.Publish), AgentwerkePolicies.Admin)]
    [InlineData(typeof(RunsController), nameof(RunsController.Start), AgentwerkePolicies.Operator)]
    [InlineData(typeof(RunsController), nameof(RunsController.Cancel), AgentwerkePolicies.Operator)]
    [InlineData(typeof(RunsController), nameof(RunsController.Recover), AgentwerkePolicies.Operator)]
    [InlineData(typeof(RunsController), nameof(RunsController.ResumeExternal), AgentwerkePolicies.Operator)]
    [InlineData(typeof(RunsController), nameof(RunsController.UploadArtifact), AgentwerkePolicies.Operator)]
    [InlineData(typeof(RunsController), nameof(RunsController.GetEvidencePack), AgentwerkePolicies.Operator)]
    [InlineData(typeof(RunsController), nameof(RunsController.DownloadEvidencePack), AgentwerkePolicies.Operator)]
    [InlineData(typeof(ApprovalsController), nameof(ApprovalsController.Decide), AgentwerkePolicies.Approver)]
    [InlineData(typeof(TemplatesController), nameof(TemplatesController.Clone), AgentwerkePolicies.Operator)]
    public void StateChangingActions_RequireExpectedPolicy(
        Type controllerType,
        string actionName,
        string expectedPolicy)
    {
        var method = controllerType.GetMethods()
            .Single(method => method.Name == actionName);

        Assert.Contains(
            method.GetCustomAttributes<AuthorizeAttribute>(),
            attribute => attribute.Policy == expectedPolicy);
    }

    [Fact]
    public void PolicyManagementController_RequiresAdminPolicy()
    {
        var attribute = Assert.Single(typeof(PoliciesController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(AgentwerkePolicies.Admin, attribute.Policy);
    }

    [Fact]
    public void SettingsController_RequiresAdminPolicy()
    {
        var attribute = Assert.Single(typeof(SettingsController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(AgentwerkePolicies.Admin, attribute.Policy);
    }
}
