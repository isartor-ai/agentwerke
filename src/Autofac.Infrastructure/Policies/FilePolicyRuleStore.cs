using Autofac.AgentSecOps;
using Microsoft.Extensions.Options;
using YamlDotNet.Serialization;
using YamlDotNet.Serialization.NamingConventions;

namespace Autofac.Infrastructure.Policies;

public sealed class FilePolicyRuleStore : IPolicyRuleStore
{
    private readonly string _filePath;
    private readonly object _sync = new();
    private readonly ISerializer _serializer;
    private readonly IDeserializer _deserializer;
    private PolicyRuleSet _current;

    public FilePolicyRuleStore(IOptions<PolicyStoreOptions> options)
    {
        _filePath = Path.GetFullPath(options.Value.FilePath);
        _serializer = new SerializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .ConfigureDefaultValuesHandling(DefaultValuesHandling.OmitNull)
            .Build();
        _deserializer = new DeserializerBuilder()
            .WithNamingConvention(CamelCaseNamingConvention.Instance)
            .IgnoreUnmatchedProperties()
            .Build();
        _current = LoadOrCreate();
    }

    public PolicyRuleSet GetSnapshot()
    {
        lock (_sync)
        {
            return Clone(_current);
        }
    }

    public PolicyRule? FindById(string id)
    {
        lock (_sync)
        {
            return Clone(_current.Rules.FirstOrDefault(rule => string.Equals(rule.Id, id, StringComparison.Ordinal)));
        }
    }

    public void Upsert(PolicyRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        lock (_sync)
        {
            var index = _current.Rules.FindIndex(candidate => string.Equals(candidate.Id, rule.Id, StringComparison.Ordinal));
            if (index >= 0)
            {
                _current.Rules[index] = Clone(rule)!;
            }
            else
            {
                _current.Rules.Add(Clone(rule)!);
            }

            StampAndPersist();
        }
    }

    public bool Delete(string id)
    {
        lock (_sync)
        {
            var removed = _current.Rules.RemoveAll(rule => string.Equals(rule.Id, id, StringComparison.Ordinal)) > 0;
            if (removed)
            {
                StampAndPersist();
            }

            return removed;
        }
    }

    private PolicyRuleSet LoadOrCreate()
    {
        if (File.Exists(_filePath))
        {
            var yaml = File.ReadAllText(_filePath);
            var loaded = _deserializer.Deserialize<PolicyRuleSet>(yaml);
            if (loaded is not null && loaded.Rules.Count > 0)
            {
                return Normalize(loaded);
            }
        }

        var defaults = Normalize(PolicyDefaults.CreateRuleSet());
        Persist(defaults);
        return defaults;
    }

    private void StampAndPersist()
    {
        _current.UpdatedAt = DateTimeOffset.UtcNow.ToString("o");
        _current.Version = _current.UpdatedAt;
        _current = Normalize(_current);
        Persist(_current);
    }

    private void Persist(PolicyRuleSet ruleSet)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_filePath)!);
        var yaml = _serializer.Serialize(ruleSet);
        File.WriteAllText(_filePath, yaml);
    }

    private static PolicyRuleSet Normalize(PolicyRuleSet ruleSet)
    {
        ruleSet.Rules = ruleSet.Rules
            .Where(rule => !string.IsNullOrWhiteSpace(rule.Id))
            .OrderBy(rule => rule.Priority)
            .ThenBy(rule => rule.Id, StringComparer.Ordinal)
            .ToList();
        return ruleSet;
    }

    private static PolicyRuleSet Clone(PolicyRuleSet ruleSet)
    {
        return new PolicyRuleSet
        {
            Version = ruleSet.Version,
            UpdatedAt = ruleSet.UpdatedAt,
            Rules = ruleSet.Rules.Select(rule => Clone(rule)!).ToList()
        };
    }

    private static PolicyRule? Clone(PolicyRule? rule)
    {
        if (rule is null)
        {
            return null;
        }

        return new PolicyRule
        {
            Id = rule.Id,
            Name = rule.Name,
            Enabled = rule.Enabled,
            Priority = rule.Priority,
            DecisionKind = rule.DecisionKind,
            Rationale = rule.Rationale,
            RiskScore = rule.RiskScore,
            RiskLevel = rule.RiskLevel,
            RiskFactors = [.. rule.RiskFactors],
            Constraints = [.. rule.Constraints],
            Predicates = rule.Predicates.Select(predicate => new PolicyRulePredicate
            {
                Field = predicate.Field,
                Match = predicate.Match,
                Values = [.. predicate.Values]
            }).ToList()
        };
    }
}
