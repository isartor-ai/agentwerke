using System.Net.Http.Json;
using System.Text.Json.Nodes;

namespace Agentwerke.E2ETests;

/// <summary>
/// Camunda 8 spike — validates Zeebe REST gateway reachability and basic BPMN deployment.
/// Only runs when CAMUNDA_ENABLED=true (set by docker-compose.e2e.yml --profile camunda).
///
/// Spike goals (issue #69):
///   1. Prove the Zeebe REST gateway responds (topology endpoint).
///   2. Prove a BPMN process can be submitted — autofac extension elements are expected
///      to be rejected by Zeebe with 400, which is also an acceptable outcome: it shows
///      the gateway is reachable and confirms that a translation layer will be needed
///      before the autofac:agentTask extensions can be used with Zeebe.
///
/// Run with:
///   docker compose -f docker/docker-compose.e2e.yml --profile camunda up --build
/// </summary>
public sealed class CamundaSpikeTests : E2ETestBase
{
    private static readonly string ZeebeRestUrl =
        Environment.GetEnvironmentVariable("ZEEBE_REST_URL") ?? "http://localhost:8088";

    [CamundaFact]
    public async Task Zeebe_TopologyEndpoint_ReturnsGatewayInfo()
    {
        using var http = new HttpClient { BaseAddress = new Uri(ZeebeRestUrl) };
        var resp = await http.GetAsync("/v2/topology");

        Assert.True(resp.IsSuccessStatusCode,
            $"Expected 200 from Zeebe /v2/topology but got {(int)resp.StatusCode}. " +
            "Check that ZEEBE_BROKER_GATEWAY_REST_ENABLED=true and the container is healthy.");

        var body = await resp.Content.ReadFromJsonAsync<JsonObject>();
        Assert.NotNull(body);

        // Zeebe topology response always contains at least one of these fields
        Assert.True(
            body!.ContainsKey("gatewayVersion") || body.ContainsKey("clusterSize") || body.ContainsKey("brokers"),
            $"Unexpected topology shape: {body.ToJsonString()}");
    }

    [CamundaFact]
    public async Task Zeebe_DeployBpmn_ReturnsSuccessOrRejection()
    {
        // The e2e-simple.bpmn uses autofac:agentTask extension elements that Zeebe
        // does not understand. A 200 means Zeebe accepted the process; a 400 means
        // Zeebe rejected the custom extensions. Both outcomes are valid for the spike:
        // they prove Zeebe is reachable. A production integration would strip/translate
        // the autofac extensions into Zeebe-native job worker task types before deploying.
        var bpmn = LoadFixture("e2e-simple.bpmn");

        using var http = new HttpClient { BaseAddress = new Uri(ZeebeRestUrl) };
        using var form = new MultipartFormDataContent();
        form.Add(
            new StringContent(bpmn, System.Text.Encoding.UTF8, "application/xml"),
            "resources",
            "e2e-simple.bpmn");

        var resp = await http.PostAsync("/v2/deployments", form);

        Assert.True(
            resp.IsSuccessStatusCode || resp.StatusCode == System.Net.HttpStatusCode.BadRequest,
            $"Unexpected Zeebe deployment status: {(int)resp.StatusCode} — " +
            $"body: {await resp.Content.ReadAsStringAsync()}");
    }

    [CamundaFact]
    public async Task Zeebe_DeployMinimalBpmn_Succeeds()
    {
        // A Zeebe-native BPMN (no autofac extensions, no service task) must deploy cleanly.
        // This proves Zeebe is functional end-to-end, not just reachable.
        const string minimalBpmn = """
            <?xml version="1.0" encoding="UTF-8"?>
            <bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                              id="spike-defs" targetNamespace="http://bpmn.io/schema/bpmn">
              <bpmn:process id="spike-minimal" name="Spike Minimal" isExecutable="true">
                <bpmn:startEvent id="Start"/>
                <bpmn:endEvent id="End"/>
                <bpmn:sequenceFlow id="flow1" sourceRef="Start" targetRef="End"/>
              </bpmn:process>
            </bpmn:definitions>
            """;

        using var http = new HttpClient { BaseAddress = new Uri(ZeebeRestUrl) };
        using var form = new MultipartFormDataContent();
        form.Add(
            new StringContent(minimalBpmn, System.Text.Encoding.UTF8, "application/xml"),
            "resources",
            "spike-minimal.bpmn");

        var resp = await http.PostAsync("/v2/deployments", form);
        var body = await resp.Content.ReadAsStringAsync();

        Assert.True(resp.IsSuccessStatusCode,
            $"Minimal Zeebe BPMN deployment failed with {(int)resp.StatusCode}: {body}");

        var json = JsonNode.Parse(body);
        Assert.NotNull(json);
        Assert.True(
            json!["deployments"] is JsonArray { Count: > 0 },
            $"Expected non-empty deployments array in response: {body}");
    }
}
