using Autofac.Api.Auth;
using Autofac.Application.Observability;
using Autofac.Domain.Persistence;
using System.Globalization;
using System.Security.Claims;
using System.Text.Json;

namespace Autofac.Api.Settings;

public sealed class SettingsService : ISettingsService
{
    private static readonly JsonSerializerOptions AuditJsonOptions = new(JsonSerializerDefaults.Web);

    private readonly IConfiguration _configuration;
    private readonly SettingsCatalog _catalog;
    private readonly ISettingsOverrideStore _overrideStore;
    private readonly ISettingsSecretStore _secretStore;
    private readonly IAuditRepository _auditRepository;
    private readonly ICorrelationContext _correlationContext;

    public SettingsService(
        IConfiguration configuration,
        SettingsCatalog catalog,
        ISettingsOverrideStore overrideStore,
        ISettingsSecretStore secretStore,
        IAuditRepository auditRepository,
        ICorrelationContext correlationContext)
    {
        _configuration = configuration;
        _catalog = catalog;
        _overrideStore = overrideStore;
        _secretStore = secretStore;
        _auditRepository = auditRepository;
        _correlationContext = correlationContext;
    }

    public async Task<SettingsSnapshotResponse> GetSnapshotAsync(CancellationToken cancellationToken)
    {
        var overrides = await _overrideStore.ReadAsync(cancellationToken);
        var categories = new List<SettingsCategoryResponse>();

        foreach (var category in _catalog.Categories)
        {
            var fields = new List<SettingsFieldResponse>();
            foreach (var field in category.Fields)
            {
                SettingsSecretStatusResponse? secret = null;
                object? value = null;
                var source = "default";

                if (field.IsSecret)
                {
                    var status = await _secretStore.GetStatusAsync(field.Path, cancellationToken);
                    secret = new SettingsSecretStatusResponse(
                        status.Configured,
                        status.Source,
                        status.Fingerprint,
                        _secretStore.CanWrite);
                    source = status.Source;
                }
                else
                {
                    value = ReadValue(field, overrides, out source);
                }

                fields.Add(new SettingsFieldResponse(
                    field.Path,
                    field.Label,
                    field.Description,
                    ToWireValueKind(field.ValueKind),
                    value,
                    field.IsSecret,
                    field.IsEditable,
                    field.RequiresRestart,
                    source,
                    field.Options,
                    secret));
            }

            categories.Add(new SettingsCategoryResponse(
                category.Id,
                category.Title,
                category.Description,
                fields));
        }

        return new SettingsSnapshotResponse(DateTimeOffset.UtcNow.ToString("o"), categories);
    }

    public async Task<SettingsUpdateResponse> UpdateAsync(
        SettingsUpdateRequest request,
        ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        var errors = new List<SettingsValidationError>();
        var normalizedValues = new Dictionary<string, JsonElement>(StringComparer.OrdinalIgnoreCase);
        var normalizedSecrets = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        foreach (var (path, rawValue) in request.Values ?? new Dictionary<string, JsonElement>())
        {
            if (!TryResolveField(path, out var field, errors))
            {
                continue;
            }

            if (field.IsSecret)
            {
                errors.Add(new SettingsValidationError(path, "Secret fields must be sent in the secrets payload."));
                continue;
            }

            if (!field.IsEditable)
            {
                errors.Add(new SettingsValidationError(path, "This setting is read-only from the Settings API."));
                continue;
            }

            if (TryNormalizeValue(field, rawValue, out var normalizedValue, out var message))
            {
                normalizedValues[field.Path] = normalizedValue;
            }
            else
            {
                errors.Add(new SettingsValidationError(path, message));
            }
        }

        foreach (var (path, secretValue) in request.Secrets ?? new Dictionary<string, string>())
        {
            if (!TryResolveField(path, out var field, errors))
            {
                continue;
            }

            if (!field.IsSecret)
            {
                errors.Add(new SettingsValidationError(path, "Non-secret fields must be sent in the values payload."));
                continue;
            }

            if (!field.IsEditable)
            {
                errors.Add(new SettingsValidationError(path, "This secret is read-only from the Settings API."));
                continue;
            }

            if (!_secretStore.CanWrite)
            {
                errors.Add(new SettingsValidationError(path, "Local secret writes are disabled for this deployment."));
                continue;
            }

            if (string.IsNullOrWhiteSpace(secretValue))
            {
                errors.Add(new SettingsValidationError(path, "Secret value cannot be empty."));
                continue;
            }

            normalizedSecrets[field.Path] = secretValue;
        }

        if (errors.Count > 0)
        {
            throw new SettingsValidationException(errors);
        }

        var overrides = await _overrideStore.ReadAsync(cancellationToken);
        foreach (var (path, value) in normalizedValues)
        {
            overrides.Values[path] = value.Clone();
        }

        if (normalizedValues.Count > 0)
        {
            await _overrideStore.SaveAsync(overrides, cancellationToken);
        }

        foreach (var (path, secretValue) in normalizedSecrets)
        {
            await _secretStore.SetSecretAsync(path, secretValue, cancellationToken);
        }

        var changedValues = normalizedValues.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var changedSecrets = normalizedSecrets.Keys.Order(StringComparer.OrdinalIgnoreCase).ToArray();
        var restartRequired = changedValues
            .Concat(changedSecrets)
            .Any(path => _catalog.TryFind(path, out var field) && field.RequiresRestart);
        var auditId = await WriteAuditAsync(
            actor,
            "settings.update",
            "success",
            new
            {
                changedValues,
                rotatedSecrets = changedSecrets,
                restartRequired
            },
            cancellationToken);

        return new SettingsUpdateResponse(
            await GetSnapshotAsync(cancellationToken),
            changedValues,
            changedSecrets,
            restartRequired,
            auditId);
    }

    public async Task<SettingsTestResponse> TestTargetAsync(
        string target,
        ClaimsPrincipal actor,
        CancellationToken cancellationToken)
    {
        var normalizedTarget = target.Trim().ToLowerInvariant();
        var messages = new List<string>();
        var succeeded = normalizedTarget switch
        {
            "github" => await ValidateGitHubAsync(messages, cancellationToken),
            "jira" => await ValidateJiraAsync(messages, cancellationToken),
            "slack" => await ValidateSecretBackedConnectorAsync("Integrations:Slack", "Slack", messages, cancellationToken),
            "teams" => await ValidateSecretBackedConnectorAsync("Integrations:Teams", "Teams", messages, cancellationToken),
            "model" or "anthropic" => await ValidateModelAsync(messages, cancellationToken),
            "camunda" => await ValidateCamundaAsync(messages, cancellationToken),
            _ => UnknownTarget(normalizedTarget, messages)
        };

        var auditId = await WriteAuditAsync(
            actor,
            "settings.test",
            succeeded ? "success" : "failure",
            new
            {
                target = normalizedTarget,
                messages
            },
            cancellationToken);

        return new SettingsTestResponse(
            normalizedTarget,
            succeeded,
            messages,
            DateTimeOffset.UtcNow.ToString("o"),
            auditId);
    }

    private bool TryResolveField(
        string path,
        out SettingsFieldDefinition field,
        List<SettingsValidationError> errors)
    {
        if (_catalog.TryFind(path, out field!))
        {
            return true;
        }

        errors.Add(new SettingsValidationError(path, "Unknown settings path."));
        return false;
    }

    private object? ReadValue(
        SettingsFieldDefinition field,
        SettingsOverrideDocument overrides,
        out string source)
    {
        if (overrides.Values.TryGetValue(field.Path, out var overrideValue))
        {
            source = "settings-overrides";
            return JsonElementToValue(field, overrideValue);
        }

        var section = _configuration.GetSection(field.Path);
        if (section.Exists())
        {
            source = "configuration";
            return ConfigurationSectionToValue(field, section);
        }

        source = "default";
        return field.DefaultValue;
    }

    private object? ConfigurationSectionToValue(SettingsFieldDefinition field, IConfigurationSection section)
    {
        return field.ValueKind switch
        {
            SettingsValueKind.Boolean => bool.TryParse(section.Value, out var value) ? value : field.DefaultValue,
            SettingsValueKind.Integer => int.TryParse(section.Value, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value)
                ? value
                : field.DefaultValue,
            SettingsValueKind.Decimal => decimal.TryParse(section.Value, NumberStyles.Number, CultureInfo.InvariantCulture, out var value)
                ? value
                : field.DefaultValue,
            SettingsValueKind.StringArray => section.GetChildren().Any()
                ? section.GetChildren().Select(child => child.Value ?? string.Empty).Where(value => value.Length > 0).ToArray()
                : SplitList(section.Value),
            SettingsValueKind.StringMap => section.GetChildren().Any()
                ? section.GetChildren().ToDictionary(child => child.Key, child => child.GetChildren().Select(grandchild => grandchild.Value ?? string.Empty).ToArray())
                : field.DefaultValue,
            _ => section.Value ?? field.DefaultValue
        };
    }

    private static object? JsonElementToValue(SettingsFieldDefinition field, JsonElement value)
    {
        return field.ValueKind switch
        {
            SettingsValueKind.Boolean => value.ValueKind == JsonValueKind.True || (value.ValueKind == JsonValueKind.String && bool.Parse(value.GetString()!)),
            SettingsValueKind.Integer => value.ValueKind == JsonValueKind.Number ? value.GetInt32() : int.Parse(value.GetString()!, CultureInfo.InvariantCulture),
            SettingsValueKind.Decimal => value.ValueKind == JsonValueKind.Number ? value.GetDecimal() : decimal.Parse(value.GetString()!, CultureInfo.InvariantCulture),
            SettingsValueKind.StringArray => value.ValueKind == JsonValueKind.Array
                ? value.EnumerateArray().Select(item => item.GetString() ?? string.Empty).Where(item => item.Length > 0).ToArray()
                : SplitList(value.GetString()),
            SettingsValueKind.StringMap => value.ValueKind == JsonValueKind.Object
                ? JsonSerializer.Deserialize<Dictionary<string, string[]>>(value.GetRawText()) ?? []
                : field.DefaultValue,
            _ => value.ValueKind == JsonValueKind.String ? value.GetString() : value.ToString()
        };
    }

    private bool TryNormalizeValue(
        SettingsFieldDefinition field,
        JsonElement value,
        out JsonElement normalizedValue,
        out string message)
    {
        normalizedValue = default;
        message = string.Empty;

        try
        {
            object normalized = field.ValueKind switch
            {
                SettingsValueKind.Boolean => NormalizeBoolean(value),
                SettingsValueKind.Integer => NormalizeInteger(field, value),
                SettingsValueKind.Decimal => NormalizeDecimal(value),
                SettingsValueKind.StringArray => NormalizeStringArray(value),
                SettingsValueKind.Url => NormalizeUrl(value),
                SettingsValueKind.Enum => NormalizeEnum(field, value),
                SettingsValueKind.String => NormalizeString(value),
                SettingsValueKind.StringMap => NormalizeStringMap(value),
                _ => throw new InvalidOperationException("Unsupported value type.")
            };

            normalizedValue = JsonSerializer.SerializeToElement(normalized);
            return true;
        }
        catch (ArgumentException ex)
        {
            message = ex.Message;
            return false;
        }
        catch (FormatException)
        {
            message = $"Value must be a valid {ToWireValueKind(field.ValueKind)}.";
            return false;
        }
        catch (InvalidOperationException)
        {
            message = $"Value must be a valid {ToWireValueKind(field.ValueKind)}.";
            return false;
        }
    }

    private static string NormalizeString(JsonElement value)
        => value.ValueKind == JsonValueKind.String
            ? value.GetString()?.Trim() ?? string.Empty
            : value.ToString().Trim();

    private static string NormalizeUrl(JsonElement value)
    {
        var text = NormalizeString(value);
        if (text.Length == 0)
        {
            return string.Empty;
        }

        return Uri.TryCreate(text, UriKind.Absolute, out _)
            ? text
            : throw new ArgumentException("Value must be an absolute URL or empty.");
    }

    private static bool NormalizeBoolean(JsonElement value)
    {
        return value.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.String when bool.TryParse(value.GetString(), out var parsed) => parsed,
            _ => throw new FormatException()
        };
    }

    private static int NormalizeInteger(SettingsFieldDefinition field, JsonElement value)
    {
        var parsed = value.ValueKind == JsonValueKind.Number
            ? value.GetInt32()
            : int.Parse(value.GetString() ?? string.Empty, CultureInfo.InvariantCulture);

        if (field.MinInt is not null && parsed < field.MinInt)
        {
            throw new ArgumentException($"Value must be greater than or equal to {field.MinInt}.");
        }

        if (field.MaxInt is not null && parsed > field.MaxInt)
        {
            throw new ArgumentException($"Value must be less than or equal to {field.MaxInt}.");
        }

        return parsed;
    }

    private static decimal NormalizeDecimal(JsonElement value)
        => value.ValueKind == JsonValueKind.Number
            ? value.GetDecimal()
            : decimal.Parse(value.GetString() ?? string.Empty, CultureInfo.InvariantCulture);

    private static string NormalizeEnum(SettingsFieldDefinition field, JsonElement value)
    {
        var text = NormalizeString(value);
        var match = field.Options.FirstOrDefault(option => string.Equals(option, text, StringComparison.OrdinalIgnoreCase));
        return match ?? throw new ArgumentException($"Value must be one of: {string.Join(", ", field.Options)}.");
    }

    private static string[] NormalizeStringArray(JsonElement value)
    {
        return value.ValueKind == JsonValueKind.Array
            ? value.EnumerateArray()
                .Select(item => item.GetString()?.Trim() ?? string.Empty)
                .Where(item => item.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : SplitList(value.GetString());
    }

    private static Dictionary<string, string[]> NormalizeStringMap(JsonElement value)
    {
        if (value.ValueKind != JsonValueKind.Object)
        {
            throw new FormatException();
        }

        return value.EnumerateObject()
            .ToDictionary(
                property => property.Name,
                property => property.Value.ValueKind == JsonValueKind.Array
                    ? property.Value.EnumerateArray()
                        .Select(item => item.GetString()?.Trim() ?? string.Empty)
                        .Where(item => item.Length > 0)
                        .ToArray()
                    : SplitList(property.Value.GetString()),
                StringComparer.OrdinalIgnoreCase);
    }

    private static string[] SplitList(string? value)
    {
        return string.IsNullOrWhiteSpace(value)
            ? []
            : value.Split(['\r', '\n', ','], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray();
    }

    private async Task<bool> ValidateGitHubAsync(List<string> messages, CancellationToken cancellationToken)
    {
        var enabled = await GetBooleanAsync("Integrations:GitHub:Enabled", cancellationToken);
        if (!enabled)
        {
            messages.Add("GitHub integration is disabled.");
            return false;
        }

        AddRequired(messages, "Integrations:GitHub:ApiBaseUrl", await GetStringAsync("Integrations:GitHub:ApiBaseUrl", cancellationToken));
        AddRequired(messages, "Integrations:GitHub:RepositoryOwner", await GetStringAsync("Integrations:GitHub:RepositoryOwner", cancellationToken));
        AddRequired(messages, "Integrations:GitHub:RepositoryName", await GetStringAsync("Integrations:GitHub:RepositoryName", cancellationToken));
        await AddRequiredSecretAsync(messages, "Integrations:GitHub:PersonalAccessToken", cancellationToken);
        if (messages.Count == 0)
        {
            messages.Add("GitHub settings are ready for outbound API calls.");
            return true;
        }

        return false;
    }

    private async Task<bool> ValidateJiraAsync(List<string> messages, CancellationToken cancellationToken)
    {
        var enabled = await GetBooleanAsync("Integrations:Jira:Enabled", cancellationToken);
        if (!enabled)
        {
            messages.Add("Jira integration is disabled.");
            return false;
        }

        AddRequired(messages, "Integrations:Jira:ApiBaseUrl", await GetStringAsync("Integrations:Jira:ApiBaseUrl", cancellationToken));
        AddRequired(messages, "Integrations:Jira:Username", await GetStringAsync("Integrations:Jira:Username", cancellationToken));
        await AddRequiredSecretAsync(messages, "Integrations:Jira:ApiToken", cancellationToken);
        if (messages.Count == 0)
        {
            messages.Add("Jira settings are ready for outbound API calls.");
            return true;
        }

        return false;
    }

    private async Task<bool> ValidateSecretBackedConnectorAsync(
        string section,
        string displayName,
        List<string> messages,
        CancellationToken cancellationToken)
    {
        var enabled = await GetBooleanAsync($"{section}:Enabled", cancellationToken);
        if (!enabled)
        {
            messages.Add($"{displayName} integration is disabled.");
            return false;
        }

        await AddRequiredSecretAsync(messages, $"{section}:WebhookUrl", cancellationToken);
        if (messages.Count == 0)
        {
            messages.Add($"{displayName} settings are ready for notification delivery.");
            return true;
        }

        return false;
    }

    private async Task<bool> ValidateModelAsync(List<string> messages, CancellationToken cancellationToken)
    {
        var provider = await GetStringAsync("Anthropic:Provider", cancellationToken);
        if (string.Equals(provider, "mock", StringComparison.OrdinalIgnoreCase))
        {
            messages.Add("Mock model provider is selected; no external credential is required.");
            return true;
        }

        AddRequired(messages, "Anthropic:ApiBaseUrl", await GetStringAsync("Anthropic:ApiBaseUrl", cancellationToken));
        AddRequired(messages, "Anthropic:Model", await GetStringAsync("Anthropic:Model", cancellationToken));
        await AddRequiredSecretAsync(messages, "Anthropic:ApiKey", cancellationToken);
        if (messages.Count == 0)
        {
            messages.Add("Model provider settings are ready for outbound API calls.");
            return true;
        }

        return false;
    }

    private async Task<bool> ValidateCamundaAsync(List<string> messages, CancellationToken cancellationToken)
    {
        var enabled = await GetBooleanAsync("Camunda:Enabled", cancellationToken);
        if (!enabled)
        {
            messages.Add("Camunda adapter is disabled.");
            return false;
        }

        AddRequired(messages, "Camunda:BaseUrl", await GetStringAsync("Camunda:BaseUrl", cancellationToken));
        var authMode = await GetStringAsync("Camunda:AuthMode", cancellationToken);
        if (string.Equals(authMode, "Basic", StringComparison.OrdinalIgnoreCase))
        {
            AddRequired(messages, "Camunda:Username", await GetStringAsync("Camunda:Username", cancellationToken));
            await AddRequiredSecretAsync(messages, "Camunda:Password", cancellationToken);
        }
        else if (string.Equals(authMode, "BearerToken", StringComparison.OrdinalIgnoreCase))
        {
            await AddRequiredSecretAsync(messages, "Camunda:BearerToken", cancellationToken);
        }

        if (messages.Count == 0)
        {
            messages.Add("Camunda settings are ready for adapter startup.");
            return true;
        }

        return false;
    }

    private static bool UnknownTarget(string target, List<string> messages)
    {
        messages.Add($"Unknown settings test target '{target}'.");
        return false;
    }

    private async Task<string> GetStringAsync(string path, CancellationToken cancellationToken)
    {
        var overrides = await _overrideStore.ReadAsync(cancellationToken);
        if (!_catalog.TryFind(path, out var field))
        {
            return string.Empty;
        }

        return Convert.ToString(ReadValue(field, overrides, out _), CultureInfo.InvariantCulture) ?? string.Empty;
    }

    private async Task<bool> GetBooleanAsync(string path, CancellationToken cancellationToken)
    {
        var overrides = await _overrideStore.ReadAsync(cancellationToken);
        if (!_catalog.TryFind(path, out var field))
        {
            return false;
        }

        return ReadValue(field, overrides, out _) is true;
    }

    private static void AddRequired(List<string> messages, string path, string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            messages.Add($"{path} is required.");
        }
    }

    private async Task AddRequiredSecretAsync(
        List<string> messages,
        string path,
        CancellationToken cancellationToken)
    {
        var status = await _secretStore.GetStatusAsync(path, cancellationToken);
        if (!status.Configured)
        {
            messages.Add($"{path} is required.");
        }
    }

    private async Task<string> WriteAuditAsync(
        ClaimsPrincipal actor,
        string action,
        string outcome,
        object details,
        CancellationToken cancellationToken)
    {
        var auditId = $"audit_{Guid.NewGuid():N}";
        await _auditRepository.AddAsync(new AuditRecord
        {
            Id = auditId,
            RunId = string.Empty,
            CorrelationId = string.IsNullOrWhiteSpace(_correlationContext.CorrelationId)
                ? null
                : _correlationContext.CorrelationId,
            ActorType = "user",
            Actor = AuthenticatedPrincipal.ResolveSubject(actor),
            Action = action,
            ResourceType = "settings",
            ResourceId = "platform",
            Outcome = outcome,
            Details = JsonSerializer.Serialize(details, AuditJsonOptions),
            Timestamp = DateTimeOffset.UtcNow.ToString("o")
        }, cancellationToken);
        await _auditRepository.SaveChangesAsync(cancellationToken);
        return auditId;
    }

    private static string ToWireValueKind(SettingsValueKind valueKind)
        => valueKind switch
        {
            SettingsValueKind.Boolean => "boolean",
            SettingsValueKind.Integer => "integer",
            SettingsValueKind.Decimal => "decimal",
            SettingsValueKind.StringArray => "string-array",
            SettingsValueKind.StringMap => "string-map",
            SettingsValueKind.Enum => "enum",
            SettingsValueKind.Secret => "secret",
            SettingsValueKind.Url => "url",
            _ => "string"
        };
}
