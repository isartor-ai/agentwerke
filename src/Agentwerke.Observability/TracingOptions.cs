namespace Agentwerke.Observability;

public sealed class TracingOptions
{
    public const string SectionName = "Tracing";

    /// <summary>OTLP endpoint, e.g. "http://jaeger:4317". Empty disables the exporter.</summary>
    public string OtlpEndpoint { get; set; } = string.Empty;

    /// <summary>Service name reported to the tracing backend.</summary>
    public string ServiceName { get; set; } = "agentwerke-api";

    /// <summary>Service version reported to the tracing backend.</summary>
    public string ServiceVersion { get; set; } = "1.0.0";
}
