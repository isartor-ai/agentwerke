using System.Net;
using System.Text;
using Agentwerke.Api.Controllers;
using Agentwerke.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Agentwerke.Api.Tests;

public sealed class CamundaRuntimeTests
{
    [Fact]
    public void CamundaOptions_Defaults_AreSafeForLocalDevelopment()
    {
        var options = new CamundaOptions();

        Assert.False(options.Enabled);
        Assert.Equal("http://localhost:8088/", options.BaseUrl);
        Assert.Equal(CamundaAuthMode.None, options.AuthMode);
        Assert.Equal(10, options.TimeoutSeconds);
        Assert.False(options.IsConfigured);
    }

    [Fact]
    public void AddAgentwerkeInfrastructure_BindsCamundaOptionsFromConfiguration()
    {
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["ConnectionStrings:Postgres"] = "Host=localhost;Database=agentwerke;Username=test;Password=test",
                ["WorkflowRuntime:Mode"] = "Camunda",
                ["Camunda:Enabled"] = "true",
                ["Camunda:BaseUrl"] = "https://camunda.example.test/",
                ["Camunda:AuthMode"] = "Basic",
                ["Camunda:Username"] = "demo",
                ["Camunda:Password"] = "secret",
                ["Camunda:TimeoutSeconds"] = "42"
            })
            .Build();

        var services = new ServiceCollection();
        services.AddAgentwerkeInfrastructure(configuration);

        using var provider = services.BuildServiceProvider();
        var options = provider.GetRequiredService<IOptions<CamundaOptions>>().Value;

        Assert.True(options.Enabled);
        Assert.Equal("https://camunda.example.test/", options.BaseUrl);
        Assert.Equal(CamundaAuthMode.Basic, options.AuthMode);
        Assert.Equal("demo", options.Username);
        Assert.Equal("secret", options.Password);
        Assert.Equal(42, options.TimeoutSeconds);
        Assert.True(options.IsConfigured);
    }

    [Fact]
    public async Task CamundaClient_GetTopologyAsync_UsesBasicAuthenticationAndTopologyPath()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(async request =>
        {
            requests.Add(await CloneAsync(request));
            return Json(HttpStatusCode.OK, """
                {
                  "brokers": [
                    {
                      "nodeId": 0,
                      "host": "camunda-zeebe-0",
                      "port": 26501,
                      "partitions": [
                        {
                          "partitionId": 1,
                          "role": "leader",
                          "health": "healthy"
                        }
                      ],
                      "version": "8.8.0"
                    }
                  ],
                  "clusterSize": 1,
                  "partitionsCount": 1,
                  "replicationFactor": 1,
                  "gatewayVersion": "8.8.0"
                }
                """);
        });

        var client = new CamundaClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://camunda.example.test/platform/") },
            Options.Create(new CamundaOptions
            {
                Enabled = true,
                BaseUrl = "https://camunda.example.test/platform/",
                AuthMode = CamundaAuthMode.Basic,
                Username = "demo",
                Password = "secret"
            }));

        var topology = await client.GetTopologyAsync(CancellationToken.None);

        Assert.Equal("8.8.0", topology.GatewayVersion);
        Assert.Equal(1, topology.ClusterSize);
        Assert.Single(topology.Brokers);

        Assert.Single(requests);
        Assert.Equal(HttpMethod.Get, requests[0].Method);
        Assert.Equal("/platform/v2/topology", requests[0].RequestUri?.AbsolutePath);
        Assert.Equal("application/json", requests[0].Headers.Accept.Single().MediaType);
        Assert.Equal("Basic", requests[0].Headers.Authorization?.Scheme);
        Assert.Equal(
            Convert.ToBase64String(Encoding.UTF8.GetBytes("demo:secret")),
            requests[0].Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task CamundaClient_GetTopologyAsync_UsesBearerAuthenticationWhenConfigured()
    {
        var requests = new List<HttpRequestMessage>();
        var handler = new StubHttpMessageHandler(async request =>
        {
            requests.Add(await CloneAsync(request));
            return Json(HttpStatusCode.OK, """
                {
                  "brokers": [],
                  "clusterSize": 0,
                  "partitionsCount": 0,
                  "replicationFactor": 0,
                  "gatewayVersion": "8.9.0"
                }
                """);
        });

        var client = new CamundaClient(
            new HttpClient(handler) { BaseAddress = new Uri("https://camunda.example.test/") },
            Options.Create(new CamundaOptions
            {
                Enabled = true,
                BaseUrl = "https://camunda.example.test/",
                AuthMode = CamundaAuthMode.BearerToken,
                BearerToken = "token-value"
            }));

        await client.GetTopologyAsync(CancellationToken.None);

        Assert.Single(requests);
        Assert.Equal("Bearer", requests[0].Headers.Authorization?.Scheme);
        Assert.Equal("token-value", requests[0].Headers.Authorization?.Parameter);
    }

    [Fact]
    public async Task CamundaRuntimeStatusService_ReturnsDisabledStatusWithoutCallingCamunda()
    {
        var service = new CamundaRuntimeStatusService(
            new ThrowingCamundaClient(),
            Options.Create(new CamundaOptions()));

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.False(status.Enabled);
        Assert.False(status.Configured);
        Assert.False(status.Reachable);
        Assert.Null(status.GatewayVersion);
    }

    [Fact]
    public async Task CamundaRuntimeStatusService_MapsTopologyToReachableStatus()
    {
        var service = new CamundaRuntimeStatusService(
            new StubCamundaClient(new CamundaTopologyResponse
            {
                Brokers =
                [
                    new CamundaBrokerResponse
                    {
                        NodeId = 0,
                        Host = "camunda-zeebe-0",
                        Port = 26501,
                        Version = "8.8.0"
                    }
                ],
                ClusterSize = 1,
                PartitionsCount = 1,
                ReplicationFactor = 1,
                GatewayVersion = "8.8.0"
            }),
            Options.Create(new CamundaOptions
            {
                Enabled = true,
                BaseUrl = "http://localhost:8088/",
                AuthMode = CamundaAuthMode.None
            }));

        var status = await service.GetStatusAsync(CancellationToken.None);

        Assert.True(status.Enabled);
        Assert.True(status.Configured);
        Assert.True(status.Reachable);
        Assert.Equal("8.8.0", status.GatewayVersion);
        Assert.Equal(1, status.BrokerCount);
        Assert.Equal(1, status.ClusterSize);
    }

    [Fact]
    public async Task HealthController_Camunda_ReturnsConfiguredAndReachableStatus()
    {
        var controller = new HealthController(
            new StubCamundaRuntimeStatusService(new CamundaRuntimeStatus(
                Enabled: true,
                Configured: true,
                Reachable: true,
                BaseUrl: "http://localhost:8088/",
                AuthMode: CamundaAuthMode.None,
                GatewayVersion: "8.8.0",
                BrokerCount: 1,
                ClusterSize: 1,
                PartitionsCount: 1,
                ReplicationFactor: 1,
                Error: null)),
            new WorkflowRuntimeOptions { Mode = WorkflowRuntimeMode.Camunda });

        var result = await controller.Camunda(CancellationToken.None);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<CamundaRuntimeStatus>(ok.Value);
        Assert.True(payload.Configured);
        Assert.True(payload.Reachable);
        Assert.Equal("8.8.0", payload.GatewayVersion);
    }

    private static HttpResponseMessage Json(HttpStatusCode statusCode, string json)
    {
        return new HttpResponseMessage(statusCode)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json")
        };
    }

    private static async Task<HttpRequestMessage> CloneAsync(HttpRequestMessage request)
    {
        var clone = new HttpRequestMessage(request.Method, request.RequestUri);

        foreach (var header in request.Headers)
        {
            clone.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }

        if (request.Content is not null)
        {
            var content = await request.Content.ReadAsStringAsync();
            clone.Content = new StringContent(content, Encoding.UTF8, request.Content.Headers.ContentType?.MediaType);
            foreach (var header in request.Content.Headers)
            {
                clone.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
        }

        return clone;
    }

    private sealed class StubHttpMessageHandler : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, Task<HttpResponseMessage>> _handler;

        public StubHttpMessageHandler(Func<HttpRequestMessage, Task<HttpResponseMessage>> handler)
        {
            _handler = handler;
        }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return _handler(request);
        }
    }

    private sealed class ThrowingCamundaClient : ICamundaClient
    {
        public Task<CamundaTopologyResponse> GetTopologyAsync(CancellationToken cancellationToken = default)
        {
            throw new InvalidOperationException("This client should not be called.");
        }
    }

    private sealed class StubCamundaClient : ICamundaClient
    {
        private readonly CamundaTopologyResponse _response;

        public StubCamundaClient(CamundaTopologyResponse response)
        {
            _response = response;
        }

        public Task<CamundaTopologyResponse> GetTopologyAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_response);
        }
    }

    private sealed class StubCamundaRuntimeStatusService : ICamundaRuntimeStatusService
    {
        private readonly CamundaRuntimeStatus _status;

        public StubCamundaRuntimeStatusService(CamundaRuntimeStatus status)
        {
            _status = status;
        }

        public Task<CamundaRuntimeStatus> GetStatusAsync(CancellationToken cancellationToken = default)
        {
            return Task.FromResult(_status);
        }
    }
}
