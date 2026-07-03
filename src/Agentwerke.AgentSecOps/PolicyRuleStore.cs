namespace Agentwerke.AgentSecOps;

public sealed class PolicyStoreOptions
{
    public const string SectionName = "Policies";

    public string FilePath { get; set; } = "./config/policies.yaml";
}

public sealed class PolicyRuleSet
{
    public string Version { get; set; } = DateTimeOffset.UtcNow.ToString("o");

    public string UpdatedAt { get; set; } = DateTimeOffset.UtcNow.ToString("o");

    public List<PolicyRule> Rules { get; set; } = [];
}

public sealed class PolicyRule
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public bool Enabled { get; set; } = true;

    public int Priority { get; set; } = 100;

    public string DecisionKind { get; set; } = "allow";

    public string Rationale { get; set; } = string.Empty;

    public int RiskScore { get; set; }

    public string RiskLevel { get; set; } = "low";

    public List<string> RiskFactors { get; set; } = [];

    public List<string> Constraints { get; set; } = [];

    public List<PolicyRulePredicate> Predicates { get; set; } = [];
}

public sealed class PolicyRulePredicate
{
    public string Field { get; set; } = PolicyRuleFields.AnyText;

    public string Match { get; set; } = PolicyRuleMatches.ContainsAny;

    public List<string> Values { get; set; } = [];
}

public static class PolicyRuleFields
{
    public const string Action = "action";
    public const string Environment = "environment";
    public const string PolicyTag = "policyTag";
    public const string PurposeType = "purposeType";
    public const string AgentName = "agentName";
    public const string RequiresEvidence = "requiresEvidence";
    public const string AnyText = "anyText";
    public const string Scope = "scope";
}

public static class PolicyRuleMatches
{
    public const string ContainsAny = "containsAny";
    public const string EqualsAny = "equalsAny";
}

public interface IPolicyRuleStore
{
    PolicyRuleSet GetSnapshot();

    PolicyRule? FindById(string id);

    void Upsert(PolicyRule rule);

    bool Delete(string id);
}

public sealed class InMemoryPolicyRuleStore : IPolicyRuleStore
{
    private readonly object _sync = new();
    private PolicyRuleSet _ruleSet;

    public InMemoryPolicyRuleStore(PolicyRuleSet? ruleSet = null)
    {
        _ruleSet = Clone(ruleSet ?? PolicyDefaults.CreateRuleSet());
    }

    public PolicyRuleSet GetSnapshot()
    {
        lock (_sync)
        {
            return Clone(_ruleSet);
        }
    }

    public PolicyRule? FindById(string id)
    {
        lock (_sync)
        {
            return Clone(_ruleSet.Rules.FirstOrDefault(rule => string.Equals(rule.Id, id, StringComparison.Ordinal)));
        }
    }

    public void Upsert(PolicyRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);

        lock (_sync)
        {
            var existing = _ruleSet.Rules.FindIndex(candidate => string.Equals(candidate.Id, rule.Id, StringComparison.Ordinal));
            if (existing >= 0)
            {
                _ruleSet.Rules[existing] = Clone(rule)!;
            }
            else
            {
                _ruleSet.Rules.Add(Clone(rule)!);
            }

            Stamp();
        }
    }

    public bool Delete(string id)
    {
        lock (_sync)
        {
            var removed = _ruleSet.Rules.RemoveAll(rule => string.Equals(rule.Id, id, StringComparison.Ordinal)) > 0;
            if (removed)
            {
                Stamp();
            }

            return removed;
        }
    }

    private void Stamp()
    {
        _ruleSet.UpdatedAt = DateTimeOffset.UtcNow.ToString("o");
        _ruleSet.Version = _ruleSet.UpdatedAt;
        _ruleSet.Rules = _ruleSet.Rules
            .OrderBy(rule => rule.Priority)
            .ThenBy(rule => rule.Id, StringComparer.Ordinal)
            .ToList();
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

public static class PolicyDefaults
{
    public static PolicyRuleSet CreateRuleSet()
    {
        var now = DateTimeOffset.UtcNow.ToString("o");

        return new PolicyRuleSet
        {
            Version = now,
            UpdatedAt = now,
            Rules =
            [
                new PolicyRule
                {
                    Id = "rule-secret-material-block",
                    Name = "Block secret and credential material access",
                    Enabled = true,
                    Priority = 10,
                    DecisionKind = "reject",
                    Rationale = "Secret and credential access actions are blocked in the MVP policy layer.",
                    RiskScore = 95,
                    RiskLevel = "critical",
                    RiskFactors =
                    [
                        "Secret or credential scope requested"
                    ],
                    Constraints =
                    [
                        "Use a dedicated secret-management workflow with human approval.",
                        "Do not expose secret material through agent output or artifacts."
                    ],
                    Predicates =
                    [
                        new PolicyRulePredicate
                        {
                            Field = PolicyRuleFields.AnyText,
                            Match = PolicyRuleMatches.ContainsAny,
                            Values = ["secret", "credential"]
                        },
                        new PolicyRulePredicate
                        {
                            Field = PolicyRuleFields.Action,
                            Match = PolicyRuleMatches.ContainsAny,
                            Values = ["access", "export", "reveal", "retrieve", "dump", "rotate"]
                        }
                    ]
                },
                new PolicyRule
                {
                    Id = "rule-production-delivery-approval",
                    Name = "Escalate production delivery actions",
                    Enabled = true,
                    Priority = 20,
                    DecisionKind = "escalate",
                    Rationale = "This action targets a sensitive delivery path and must be approved before execution.",
                    RiskScore = 78,
                    RiskLevel = "high",
                    Constraints =
                    [
                        "Require human approval before execution.",
                        "Capture rollout and rollback evidence in the run record."
                    ],
                    Predicates =
                    [
                        new PolicyRulePredicate
                        {
                            Field = PolicyRuleFields.Scope,
                            Match = PolicyRuleMatches.ContainsAny,
                            Values = ["prod", "production"]
                        },
                        new PolicyRulePredicate
                        {
                            Field = PolicyRuleFields.Action,
                            Match = PolicyRuleMatches.ContainsAny,
                            Values = ["deploy", "promote", "rollback", "merge"]
                        }
                    ]
                }
            ]
        };
    }
}
