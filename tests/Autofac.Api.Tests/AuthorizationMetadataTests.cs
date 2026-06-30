using Autofac.Api.Auth;
using Autofac.Api.Controllers;
using Microsoft.AspNetCore.Authorization;
using System.Reflection;

namespace Autofac.Api.Tests;

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

        Assert.Equal(AutofacPolicies.Viewer, attribute.Policy);
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
    [InlineData(typeof(AgentsController), nameof(AgentsController.Upsert), AutofacPolicies.Admin)]
    [InlineData(typeof(AgentsController), nameof(AgentsController.Upload), AutofacPolicies.Admin)]
    [InlineData(typeof(WorkflowsController), nameof(WorkflowsController.Import), AutofacPolicies.Operator)]
    [InlineData(typeof(WorkflowsController), nameof(WorkflowsController.Validate), AutofacPolicies.Operator)]
    [InlineData(typeof(WorkflowsController), nameof(WorkflowsController.PolicySimulation), AutofacPolicies.Operator)]
    [InlineData(typeof(WorkflowsController), nameof(WorkflowsController.Publish), AutofacPolicies.Admin)]
    [InlineData(typeof(RunsController), nameof(RunsController.Start), AutofacPolicies.Operator)]
    [InlineData(typeof(RunsController), nameof(RunsController.Cancel), AutofacPolicies.Operator)]
    [InlineData(typeof(RunsController), nameof(RunsController.Recover), AutofacPolicies.Operator)]
    [InlineData(typeof(RunsController), nameof(RunsController.ResumeExternal), AutofacPolicies.Operator)]
    [InlineData(typeof(RunsController), nameof(RunsController.UploadArtifact), AutofacPolicies.Operator)]
    [InlineData(typeof(RunsController), nameof(RunsController.GetEvidencePack), AutofacPolicies.Operator)]
    [InlineData(typeof(RunsController), nameof(RunsController.DownloadEvidencePack), AutofacPolicies.Operator)]
    [InlineData(typeof(ApprovalsController), nameof(ApprovalsController.Decide), AutofacPolicies.Approver)]
    [InlineData(typeof(TemplatesController), nameof(TemplatesController.Clone), AutofacPolicies.Operator)]
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

        Assert.Equal(AutofacPolicies.Admin, attribute.Policy);
    }

    [Fact]
    public void SettingsController_RequiresAdminPolicy()
    {
        var attribute = Assert.Single(typeof(SettingsController).GetCustomAttributes<AuthorizeAttribute>());

        Assert.Equal(AutofacPolicies.Admin, attribute.Policy);
    }
}
