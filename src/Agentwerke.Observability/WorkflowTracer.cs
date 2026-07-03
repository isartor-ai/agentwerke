using System.Diagnostics;
using Agentwerke.Application.Observability;
using OpenTelemetry.Trace;

namespace Agentwerke.Observability;

public sealed class WorkflowTracer : IWorkflowTracer
{
    public ISpan StartSpan(string name)
    {
        var activity = WorkflowActivitySource.Instance.StartActivity(name, ActivityKind.Internal);
        return new ActivitySpan(activity);
    }

    private sealed class ActivitySpan : ISpan
    {
        private readonly Activity? _activity;

        public ActivitySpan(Activity? activity)
        {
            _activity = activity;
        }

        public void SetTag(string key, string value) => _activity?.SetTag(key, value);

        public void SetError(Exception ex)
        {
            if (_activity is null) return;
            _activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            _activity.AddException(ex);
        }

        public void Dispose() => _activity?.Dispose();
    }
}
