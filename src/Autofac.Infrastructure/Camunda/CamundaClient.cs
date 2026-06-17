using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Autofac.Infrastructure;

public sealed class CamundaClient : ICamundaClient
{
    private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _httpClient;
    private readonly IOptions<CamundaOptions> _options;

    public CamundaClient(HttpClient httpClient, IOptions<CamundaOptions> options)
    {
        _httpClient = httpClient;
        _options = options;
    }

    public async Task<CamundaTopologyResponse> GetTopologyAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "v2/topology");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Authorization = BuildAuthorizationHeader(_options.Value);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        response.EnsureSuccessStatusCode();

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var topology = await JsonSerializer.DeserializeAsync<CamundaTopologyResponse>(
            stream,
            SerializerOptions,
            cancellationToken);

        return topology ?? throw new InvalidOperationException("Camunda topology response was empty.");
    }

    public async Task<CamundaDeploymentResponse> DeployWorkflowAsync(
        CamundaDeploymentRequest deployment,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(deployment);

        using var request = new HttpRequestMessage(HttpMethod.Post, "v2/deployments");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Authorization = BuildAuthorizationHeader(_options.Value);

        using var form = new MultipartFormDataContent();
        using var resourceContent = new StringContent(deployment.BpmnXml, Encoding.UTF8, "application/xml");
        form.Add(resourceContent, "resources", deployment.ResourceName);
        request.Content = form;

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateApiExceptionAsync(response, cancellationToken);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<CamundaDeploymentResponse>(
            stream,
            SerializerOptions,
            cancellationToken);

        return result ?? throw new InvalidOperationException("Camunda deployment response was empty.");
    }

    public async Task<CamundaProcessStartResponse> StartProcessInstanceAsync(
        CamundaProcessStartRequest requestModel,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestModel);

        using var request = new HttpRequestMessage(HttpMethod.Post, "v2/process-instances");
        request.Headers.Accept.ParseAdd("application/json");
        request.Headers.Authorization = BuildAuthorizationHeader(_options.Value);
        request.Content = JsonContent.Create(requestModel, options: SerializerOptions);

        using var response = await _httpClient.SendAsync(
            request,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);

        if (!response.IsSuccessStatusCode)
        {
            throw await CreateApiExceptionAsync(response, cancellationToken);
        }

        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        var result = await JsonSerializer.DeserializeAsync<CamundaProcessStartResponse>(
            stream,
            SerializerOptions,
            cancellationToken);

        return result ?? throw new InvalidOperationException("Camunda process start response was empty.");
    }

    private static AuthenticationHeaderValue? BuildAuthorizationHeader(CamundaOptions options)
    {
        return options.AuthMode switch
        {
            CamundaAuthMode.Basic => new AuthenticationHeaderValue(
                "Basic",
                Convert.ToBase64String(Encoding.UTF8.GetBytes($"{options.Username}:{options.Password}"))),
            CamundaAuthMode.BearerToken => new AuthenticationHeaderValue("Bearer", options.BearerToken),
            _ => null
        };
    }

    private static async Task<CamundaApiException> CreateApiExceptionAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        var body = await response.Content.ReadAsStringAsync(cancellationToken);
        CamundaProblemDetails? problem = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                problem = JsonSerializer.Deserialize<CamundaProblemDetails>(body, SerializerOptions);
            }
            catch (JsonException)
            {
                problem = null;
            }
        }

        var message = !string.IsNullOrWhiteSpace(problem?.Title)
            ? problem.Title
            : $"Camunda API request failed with status code {(int)response.StatusCode}.";

        return new CamundaApiException(
            message,
            response.StatusCode,
            problem,
            body);
    }
}
