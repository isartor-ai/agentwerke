using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Autofac.Infrastructure.Workers;

public sealed class CamundaAgentJobDispatcher
{
    private const string AutofacAgentJobType = "autofac.agent";
    private const int ActivationTimeoutMilliseconds = 300_000;
    private const int MaxJobsToActivate = 1;
    private static readonly string WorkerId = $"autofac-{Environment.MachineName}-{Guid.NewGuid():N}";

    private readonly ICamundaClient _camundaClient;
    private readonly ICamundaAgentJobExecutor _jobExecutor;
    private readonly IOptions<CamundaOptions> _options;
    private readonly ILogger<CamundaAgentJobDispatcher> _logger;

    public CamundaAgentJobDispatcher(
        ICamundaClient camundaClient,
        ICamundaAgentJobExecutor jobExecutor,
        IOptions<CamundaOptions> options,
        ILogger<CamundaAgentJobDispatcher> logger)
    {
        _camundaClient = camundaClient;
        _jobExecutor = jobExecutor;
        _options = options;
        _logger = logger;
    }

    public async Task<int> ProcessNextBatchAsync(CancellationToken cancellationToken)
    {
        if (!_options.Value.IsConfigured)
        {
            return 0;
        }

        var jobs = await _camundaClient.ActivateJobsAsync(
            new CamundaJobActivationRequest(
                Type: AutofacAgentJobType,
                Worker: WorkerId,
                Timeout: ActivationTimeoutMilliseconds,
                MaxJobsToActivate: MaxJobsToActivate,
                FetchVariables: ["autofac", "input", "output"]),
            cancellationToken);

        if (jobs.Count == 0)
        {
            return 0;
        }

        _logger.LogInformation(
            "Activated {Count} Camunda job(s) for worker {WorkerId}",
            jobs.Count,
            WorkerId);

        foreach (var job in jobs)
        {
            await _jobExecutor.ExecuteAsync(job, cancellationToken);
        }

        return jobs.Count;
    }
}
