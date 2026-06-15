namespace Autofac.Api.Auth;

public sealed class JwtOptions
{
    public const string Section = "Jwt";

    public string? Issuer { get; set; }
    public string? Audience { get; set; }
    public string? SecretKey { get; set; }
    public string? Authority { get; set; }
    public bool DevTokensEnabled { get; set; }
}
