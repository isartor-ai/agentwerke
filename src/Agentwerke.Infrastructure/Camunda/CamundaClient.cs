using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Options;

namespace Agentwerke.Infrastructure;

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
}
