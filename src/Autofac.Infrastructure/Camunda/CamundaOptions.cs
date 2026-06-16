namespace Autofac.Infrastructure;

public sealed class CamundaOptions
{
    public const string Section = "Camunda";

    public bool Enabled { get; set; }

    public string BaseUrl { get; set; } = "http://localhost:8088/";

    public CamundaAuthMode AuthMode { get; set; } = CamundaAuthMode.None;

    public string Username { get; set; } = string.Empty;

    public string Password { get; set; } = string.Empty;

    public string BearerToken { get; set; } = string.Empty;

    public int TimeoutSeconds { get; set; } = 10;

    public bool IsConfigured => Enabled && HasValidBaseUrl() && HasValidAuthentication();

    internal bool HasValidBaseUrl()
    {
        return Uri.TryCreate(BaseUrl, UriKind.Absolute, out _);
    }

    internal bool HasValidAuthentication()
    {
        return AuthMode switch
        {
            CamundaAuthMode.None => true,
            CamundaAuthMode.Basic => !string.IsNullOrWhiteSpace(Username)
                && !string.IsNullOrWhiteSpace(Password),
            CamundaAuthMode.BearerToken => !string.IsNullOrWhiteSpace(BearerToken),
            _ => false
        };
    }
}

public enum CamundaAuthMode
{
    None,
    Basic,
    BearerToken
}
