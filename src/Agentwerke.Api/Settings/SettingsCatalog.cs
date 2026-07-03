namespace Agentwerke.Api.Settings;

public sealed class SettingsCatalog
{
    public static readonly SettingsCatalog Default = new(
    [
        new SettingsCategoryDefinition(
            "model",
            "Model",
            "Language-model provider, runtime limits, retries, and credential status.",
            [
                Field("Anthropic:Provider", "Provider", "Language-model backend selection.", SettingsValueKind.Enum, "auto", ["auto", "anthropic", "openai", "litellm", "mock"]),
                Secret("Anthropic:ApiKey", "API key", "Provider API key used for model calls."),
                Field("Anthropic:ApiBaseUrl", "API base URL", "Provider API endpoint.", SettingsValueKind.Url, "https://api.anthropic.com/"),
                Field("Anthropic:Model", "Model", "Default model used by agent-model runners.", SettingsValueKind.String, "claude-sonnet-4-6"),
                Field("Anthropic:MaxTokens", "Max tokens", "Maximum output tokens per model call.", SettingsValueKind.Integer, 4096, minInt: 1, maxInt: 200000),
                Field("Anthropic:MaxToolIterations", "Max tool iterations", "Tool-loop guardrail per agent step.", SettingsValueKind.Integer, 10, minInt: 0, maxInt: 100),
                Field("Anthropic:TimeoutSeconds", "Timeout seconds", "Per-request HTTP timeout.", SettingsValueKind.Integer, 100, minInt: 1, maxInt: 600),
                Field("Anthropic:MaxRetries", "Max retries", "Transient retry count for model calls.", SettingsValueKind.Integer, 3, minInt: 0, maxInt: 20),
                Field("Anthropic:RetryBaseDelayMs", "Retry base delay", "Base retry backoff in milliseconds.", SettingsValueKind.Integer, 500, minInt: 0, maxInt: 60000),
                Field("Anthropic:RetryMaxDelayMs", "Retry max delay", "Maximum retry backoff in milliseconds.", SettingsValueKind.Integer, 20000, minInt: 0, maxInt: 600000),
                Field("Anthropic:EnablePromptCaching", "Prompt caching", "Enable provider prompt caching when supported.", SettingsValueKind.Boolean, true)
            ]),

        new SettingsCategoryDefinition(
            "integrations",
            "Integrations",
            "External connector enablement, routing defaults, and webhook credentials.",
            [
                Field("Integrations:Notifications:OnApprovalRequested", "Approval notifications", "Notify chat channels when a run reaches a human approval gate.", SettingsValueKind.Boolean, true),
                Field("Integrations:Slack:Enabled", "Slack enabled", "Enable Slack connector delivery.", SettingsValueKind.Boolean, false),
                Secret("Integrations:Slack:WebhookUrl", "Slack webhook URL", "Incoming Slack webhook URL."),
                Field("Integrations:Teams:Enabled", "Teams enabled", "Enable Teams connector delivery.", SettingsValueKind.Boolean, false),
                Secret("Integrations:Teams:WebhookUrl", "Teams webhook URL", "Incoming Teams webhook URL."),
                Field("Integrations:Jira:Enabled", "Jira enabled", "Enable Jira webhook ingestion and connector calls.", SettingsValueKind.Boolean, false),
                Field("Integrations:Jira:ApiBaseUrl", "Jira API base URL", "Atlassian site API endpoint.", SettingsValueKind.Url, "https://your-domain.atlassian.net/"),
                Field("Integrations:Jira:Username", "Jira username", "Account username for Jira API calls.", SettingsValueKind.String, string.Empty),
                Secret("Integrations:Jira:ApiToken", "Jira API token", "Jira API token."),
                Secret("Integrations:Jira:WebhookSecret", "Jira webhook secret", "Shared secret for Jira webhook validation."),
                Field("Integrations:Jira:TriggerEvents", "Jira trigger events", "Jira webhook events that start workflows.", SettingsValueKind.StringArray, new[] { "jira:issue_created" }),
                Field("Integrations:GitHub:Enabled", "GitHub enabled", "Enable GitHub webhook ingestion and connector calls.", SettingsValueKind.Boolean, false),
                Field("Integrations:GitHub:ApiBaseUrl", "GitHub API base URL", "GitHub REST API endpoint.", SettingsValueKind.Url, "https://api.github.com/"),
                Field("Integrations:GitHub:RepositoryOwner", "Repository owner", "Default repository owner for outbound actions.", SettingsValueKind.String, string.Empty),
                Field("Integrations:GitHub:RepositoryName", "Repository name", "Default repository name for outbound actions.", SettingsValueKind.String, string.Empty),
                Secret("Integrations:GitHub:PersonalAccessToken", "GitHub token", "Personal access token or GitHub App token."),
                Secret("Integrations:GitHub:WebhookSecret", "GitHub webhook secret", "Shared secret for GitHub webhook validation."),
                Field("Integrations:GitHub:DefaultBaseBranch", "Default base branch", "Base branch for Agentwerke-created pull requests.", SettingsValueKind.String, "main"),
                Field("Integrations:GitHub:BranchPrefix", "Branch prefix", "Prefix for Agentwerke-created branch names.", SettingsValueKind.String, "agentwerke/run-"),
                Field("Integrations:GitHub:CreateDraftPullRequests", "Draft pull requests", "Create pull requests as drafts by default.", SettingsValueKind.Boolean, true),
                Field("Integrations:GitHub:TriggerActions", "GitHub trigger actions", "GitHub issue actions that start workflows.", SettingsValueKind.StringArray, new[] { "opened" }),
                Field("Integrations:GitHub:RequiredLabel", "GitHub required label", "Label an issue must carry to start a run. Empty disables the check.", SettingsValueKind.String, "agentwerke"),
                Field("Integrations:GitHub:DeployWorkflowFileName", "Deploy workflow file", "Default GitHub Actions workflow dispatched for deploy gates.", SettingsValueKind.String, "deploy-to-test.yml")
            ]),

        new SettingsCategoryDefinition(
            "authentication",
            "Authentication",
            "OIDC/JWT mode, development auth controls, and role claim mapping.",
            [
                Field("Jwt:Authority", "OIDC authority", "OIDC issuer authority. Leave empty for symmetric JWT mode.", SettingsValueKind.Url, string.Empty),
                Field("Jwt:Issuer", "Issuer", "Expected JWT issuer.", SettingsValueKind.String, string.Empty),
                Field("Jwt:Audience", "Audience", "Expected JWT audience.", SettingsValueKind.String, string.Empty),
                Secret("Jwt:SecretKey", "JWT signing key", "Symmetric JWT signing key."),
                Field("Jwt:DevTokensEnabled", "Development tokens", "Allow issuing development JWTs.", SettingsValueKind.Boolean, false),
                Field("Jwt:DevIdentityEnabled", "Development identity", "Enable the local development identity handler.", SettingsValueKind.Boolean, false),
                Field("Jwt:DevIdentitySubject", "Development subject", "Subject used by the local development identity.", SettingsValueKind.String, "dev:admin"),
                Field("Jwt:DevIdentityRoles", "Development roles", "Roles granted to the local development identity.", SettingsValueKind.StringArray, new[] { "Admin" }),
                Field("Jwt:RoleClaimTypes", "Role claim types", "Claim types inspected for role values.", SettingsValueKind.StringArray, new[] { "role", "roles", "groups" }),
                Field("Jwt:NameClaimTypes", "Name claim types", "Claim types inspected for display names.", SettingsValueKind.StringArray, new[] { "name", "preferred_username", "email" }),
                Field("Jwt:RoleMappings", "Role mappings", "External group-to-role mappings (also applied to LDAP groups). Edit as deployment JSON for now.", SettingsValueKind.StringMap, null, isEditable: false),
                Field("Ldap:Enabled", "LDAP enabled", "Enable LDAP/AD directory-group integration.", SettingsValueKind.Boolean, false),
                Field("Ldap:Host", "LDAP host", "Directory server hostname.", SettingsValueKind.String, string.Empty),
                Field("Ldap:BaseDn", "LDAP base DN", "Search base for user lookups (e.g. ou=Users,dc=corp,dc=example).", SettingsValueKind.String, string.Empty),
                Field("Ldap:BindDn", "LDAP bind DN", "Service-account DN used to bind before searching.", SettingsValueKind.String, string.Empty),
                Secret("Ldap:BindPassword", "LDAP bind password", "Service-account password for the bind DN."),
                Field("Ldap:UserSearchFilter", "LDAP user filter", "User search filter; {0} is the username (e.g. (sAMAccountName={0})).", SettingsValueKind.String, "(sAMAccountName={0})")
            ]),

        new SettingsCategoryDefinition(
            "runtime",
            "Runtime",
            "Workflow runtime, policy store, and optional Camunda adapter settings.",
            [
                Field("WorkflowRuntime:Mode", "Workflow runtime", "Runtime adapter used for workflow execution. Agentwerke is the default; Camunda is an opt-in enterprise adapter.", SettingsValueKind.Enum, "Agentwerke", ["Agentwerke", "Camunda"]),
                Field("Policies:FilePath", "Policy file", "Policy rule file path.", SettingsValueKind.String, "./config/policies.yaml"),
                Field("Camunda:Enabled", "Camunda enabled", "Enable the optional Camunda runtime adapter.", SettingsValueKind.Boolean, false),
                Field("Camunda:BaseUrl", "Camunda base URL", "Camunda REST API endpoint.", SettingsValueKind.Url, "http://localhost:8088/"),
                Field("Camunda:AuthMode", "Camunda auth mode", "Authentication mode for Camunda calls.", SettingsValueKind.Enum, "None", ["None", "Basic", "BearerToken"]),
                Field("Camunda:Username", "Camunda username", "Username for Camunda basic authentication.", SettingsValueKind.String, string.Empty),
                Secret("Camunda:Password", "Camunda password", "Password for Camunda basic authentication."),
                Secret("Camunda:BearerToken", "Camunda bearer token", "Bearer token for Camunda authentication."),
                Field("Camunda:TimeoutSeconds", "Camunda timeout", "HTTP timeout for Camunda calls.", SettingsValueKind.Integer, 10, minInt: 1, maxInt: 300)
            ]),

        new SettingsCategoryDefinition(
            "storage",
            "Storage",
            "Evidence and artifact storage provider settings.",
            [
                Field("ConnectionStrings:Postgres", "Postgres connection", "Database connection string. Use deployment secrets for production.", SettingsValueKind.String, string.Empty, isEditable: false),
                Field("Storage:Provider", "Storage provider", "Artifact storage backend.", SettingsValueKind.Enum, "filesystem", ["filesystem", "s3"]),
                Field("Storage:RootPath", "Filesystem root", "Local artifact storage path.", SettingsValueKind.String, "./storage"),
                Field("Storage:S3:BucketName", "S3 bucket", "S3 bucket for artifact storage.", SettingsValueKind.String, string.Empty),
                Field("Storage:S3:Region", "S3 region", "S3 region.", SettingsValueKind.String, "us-east-1"),
                Field("Storage:S3:ServiceUrl", "S3 service URL", "Custom S3-compatible service URL.", SettingsValueKind.Url, string.Empty),
                Field("Storage:S3:AccessKey", "S3 access key", "S3 access key id.", SettingsValueKind.String, string.Empty),
                Secret("Storage:S3:SecretKey", "S3 secret key", "S3 secret access key."),
                Field("Storage:S3:ForcePathStyle", "S3 path style", "Use path-style S3 addressing.", SettingsValueKind.Boolean, true)
            ]),

        new SettingsCategoryDefinition(
            "sandbox",
            "Sandbox",
            "Sandbox execution provider, resource defaults, and OpenSandbox credentials.",
            [
                Field("Sandboxes:Enabled", "Sandbox execution", "Global opt-in for sandbox-backed execution.", SettingsValueKind.Boolean, false),
                Field("Sandboxes:Provider", "Sandbox provider", "Configured sandbox provider.", SettingsValueKind.Enum, "docker", ["docker", "opensandbox", "kubernetes-kata"]),
                Field("Sandboxes:Docker:Enabled", "Docker enabled", "Enable Docker sandbox provider.", SettingsValueKind.Boolean, false),
                Field("Sandboxes:Docker:DefaultImage", "Docker default image", "Default Docker image.", SettingsValueKind.String, "alpine:3.19"),
                Field("Sandboxes:Docker:TimeoutSeconds", "Docker timeout", "Docker execution timeout in seconds.", SettingsValueKind.Integer, 60, minInt: 1, maxInt: 3600),
                Field("Sandboxes:Docker:MemoryLimitMb", "Docker memory MB", "Docker memory limit in MB.", SettingsValueKind.Integer, 256, minInt: 64, maxInt: 1048576),
                Field("Sandboxes:Docker:CpuQuota", "Docker CPU quota", "Docker CPU quota in microseconds per 100ms.", SettingsValueKind.Integer, 50000, minInt: 0, maxInt: 1000000),
                Field("Sandboxes:Docker:DockerEndpoint", "Docker endpoint", "Docker daemon endpoint.", SettingsValueKind.String, "unix:///var/run/docker.sock"),
                Field("Sandboxes:OpenSandbox:Enabled", "OpenSandbox enabled", "Enable OpenSandbox provider.", SettingsValueKind.Boolean, false),
                Field("Sandboxes:OpenSandbox:BaseUrl", "OpenSandbox base URL", "OpenSandbox API endpoint.", SettingsValueKind.Url, "http://localhost:8080/v1"),
                Secret("Sandboxes:OpenSandbox:ApiKey", "OpenSandbox API key", "OpenSandbox API key."),
                Field("Sandboxes:OpenSandbox:DefaultImage", "OpenSandbox default image", "Default image for OpenSandbox commands.", SettingsValueKind.String, "alpine:3.19"),
                Field("Sandboxes:OpenSandbox:DefaultTimeoutSeconds", "OpenSandbox timeout", "Default OpenSandbox timeout in seconds.", SettingsValueKind.Integer, 60, minInt: 1, maxInt: 3600),
                Field("Sandboxes:OpenSandbox:DefaultMemoryLimitMb", "OpenSandbox memory MB", "Default OpenSandbox memory in MB.", SettingsValueKind.Integer, 256, minInt: 64, maxInt: 1048576),
                Field("Sandboxes:OpenSandbox:DefaultCpuMilliCores", "OpenSandbox CPU millicores", "Default OpenSandbox CPU budget.", SettingsValueKind.Integer, 500, minInt: 1, maxInt: 100000),
                Field("Sandboxes:KubernetesKata:Enabled", "Kata enabled", "Enable Kubernetes Kata provider.", SettingsValueKind.Boolean, false),
                Field("Sandboxes:KubernetesKata:RuntimeClassName", "Kata runtime class", "Kubernetes RuntimeClass for Kata.", SettingsValueKind.String, "kata"),
                Field("Sandboxes:KubernetesKata:Namespace", "Kata namespace", "Kubernetes namespace for Kata sandboxes.", SettingsValueKind.String, "default")
            ]),

        new SettingsCategoryDefinition(
            "observability",
            "Observability",
            "Tracing and settings-control-plane storage.",
            [
                Field("Tracing:OtlpEndpoint", "OTLP endpoint", "OTLP exporter endpoint. Empty disables tracing export.", SettingsValueKind.Url, string.Empty),
                Field("Tracing:ServiceName", "Service name", "Service name reported to tracing backends.", SettingsValueKind.String, "agentwerke-api"),
                Field("Tracing:ServiceVersion", "Service version", "Service version reported to tracing backends.", SettingsValueKind.String, "1.0.0"),
                Field("Settings:OverridesPath", "Overrides path", "File path for persisted non-secret settings overrides.", SettingsValueKind.String, "config/settings.overrides.json", isEditable: false),
                Field("Settings:SecretsPath", "Secrets path", "File path for local secret material. Prefer external secret stores in production.", SettingsValueKind.String, "config/settings.secrets.json", isEditable: false),
                Field("Settings:AllowLocalSecretWrites", "Local secret writes", "Allow Settings to write local secret material.", SettingsValueKind.Boolean, true)
            ])
    ]);

    private readonly Dictionary<string, SettingsFieldDefinition> _fields;

    public SettingsCatalog(IReadOnlyList<SettingsCategoryDefinition> categories)
    {
        Categories = categories;
        _fields = Categories
            .SelectMany(category => category.Fields)
            .ToDictionary(field => field.Path, StringComparer.OrdinalIgnoreCase);
    }

    public IReadOnlyList<SettingsCategoryDefinition> Categories { get; }

    public bool TryFind(string path, out SettingsFieldDefinition definition)
        => _fields.TryGetValue(path, out definition!);

    private static SettingsFieldDefinition Field(
        string path,
        string label,
        string description,
        SettingsValueKind valueKind,
        object? defaultValue,
        IReadOnlyList<string>? options = null,
        bool isEditable = true,
        int? minInt = null,
        int? maxInt = null)
        => new(path, label, description, valueKind, false, isEditable, true, defaultValue, options ?? [], minInt, maxInt);

    private static SettingsFieldDefinition Secret(string path, string label, string description)
        => new(path, label, description, SettingsValueKind.Secret, true, true, true, null, [], null, null);
}

public sealed record SettingsCategoryDefinition(
    string Id,
    string Title,
    string Description,
    IReadOnlyList<SettingsFieldDefinition> Fields);

public sealed record SettingsFieldDefinition(
    string Path,
    string Label,
    string Description,
    SettingsValueKind ValueKind,
    bool IsSecret,
    bool IsEditable,
    bool RequiresRestart,
    object? DefaultValue,
    IReadOnlyList<string> Options,
    int? MinInt,
    int? MaxInt);

public enum SettingsValueKind
{
    String,
    Url,
    Boolean,
    Integer,
    Decimal,
    StringArray,
    StringMap,
    Enum,
    Secret
}
