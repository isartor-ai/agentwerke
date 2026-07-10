using System.Text.RegularExpressions;

namespace Agentwerke.Workflows.Runtime;

/// <summary>
/// Evaluates sequence-flow condition expressions against run-context variables.
///
/// Grammar — a boolean literal or a single binary comparison:
///   expression := literal | operand op operand
///   literal    := true | false | yes | no | ${true} | ${false}
///   op         := == | != | contains
///   operand    := "double-quoted" | 'single-quoted' | {{variable}} | bare-token
///
/// Operands are parsed before variables are resolved, so step outputs containing
/// quotes or operator characters can never change the expression's structure.
/// Unresolvable variables become the empty string; comparisons are ordinal.
/// An unparseable expression evaluates to false (fail closed) — the validator
/// rejects such expressions at publish time via <see cref="TryParse"/>.
/// </summary>
public static partial class ConditionExpressionEvaluator
{
    /// <summary>Longest operand excerpt echoed back in <see cref="ConditionEvaluation.Detail"/>.</summary>
    private const int DetailOperandMaxLength = 120;

    [GeneratedRegex(
        """^\s*(?<lhs>"[^"]*"|'[^']*'|\{\{\s*[a-zA-Z0-9_.-]+\s*\}\}|[^\s"'=!]+)\s*(?<op>==|!=|contains)\s*(?<rhs>"[^"]*"|'[^']*'|\{\{\s*[a-zA-Z0-9_.-]+\s*\}\}|[^\s"'=!]+)\s*$""",
        RegexOptions.CultureInvariant)]
    private static partial Regex ComparisonPattern();

    [GeneratedRegex(
        """^\{\{\s*(?<name>[a-zA-Z0-9_.-]+)\s*\}\}$""",
        RegexOptions.CultureInvariant)]
    private static partial Regex VariablePattern();

    /// <summary>
    /// Checks the expression against the grammar without evaluating it.
    /// Returns false with a human-readable reason when the expression is malformed.
    /// </summary>
    public static bool TryParse(string? expression, out string? error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(expression))
        {
            error = "Condition expression is empty.";
            return false;
        }

        if (IsBooleanLiteral(expression.Trim(), out _))
            return true;

        if (ComparisonPattern().IsMatch(expression))
            return true;

        error = "Condition must be a boolean literal (true/false) or a single comparison: " +
                "<operand> ==|!=|contains <operand>, where an operand is a quoted string, " +
                "a {{variable}}, or a bare token.";
        return false;
    }

    /// <summary>
    /// Evaluates the expression. <paramref name="resolveVariable"/> maps a variable
    /// name (e.g. "output.RunTests") to its run-context value, or null when absent.
    /// </summary>
    public static ConditionEvaluation Evaluate(string? expression, Func<string, string?> resolveVariable)
    {
        if (string.IsNullOrWhiteSpace(expression))
            return new ConditionEvaluation(false, null);

        var trimmed = expression.Trim();
        if (IsBooleanLiteral(trimmed, out var literal))
            return new ConditionEvaluation(literal, trimmed);

        var match = ComparisonPattern().Match(expression);
        if (!match.Success)
            return new ConditionEvaluation(false, "unparseable expression (fails closed)");

        var lhs = ResolveOperand(match.Groups["lhs"].Value, resolveVariable);
        var rhs = ResolveOperand(match.Groups["rhs"].Value, resolveVariable);
        var op = match.Groups["op"].Value;

        var result = op switch
        {
            "==" => string.Equals(lhs, rhs, StringComparison.Ordinal),
            "!=" => !string.Equals(lhs, rhs, StringComparison.Ordinal),
            "contains" => lhs.Contains(rhs, StringComparison.Ordinal),
            _ => false,
        };

        var detail = $"{FormatOperand(lhs)} {op} {FormatOperand(rhs)} -> {(result ? "true" : "false")}";
        return new ConditionEvaluation(result, detail);
    }

    private static bool IsBooleanLiteral(string expression, out bool value)
    {
        switch (expression)
        {
            case "true" or "${true}" or "yes":
                value = true;
                return true;
            case "false" or "${false}" or "no":
                value = false;
                return true;
            default:
                value = false;
                return false;
        }
    }

    private static string ResolveOperand(string operand, Func<string, string?> resolveVariable)
    {
        if (operand.Length >= 2 &&
            ((operand[0] == '"' && operand[^1] == '"') || (operand[0] == '\'' && operand[^1] == '\'')))
        {
            return operand[1..^1];
        }

        var variable = VariablePattern().Match(operand);
        if (variable.Success)
            return resolveVariable(variable.Groups["name"].Value) ?? string.Empty;

        return operand;
    }

    private static string FormatOperand(string value)
    {
        var excerpt = value.Length <= DetailOperandMaxLength
            ? value
            : value[..DetailOperandMaxLength] + "…";
        return $"\"{excerpt.ReplaceLineEndings(" ")}\"";
    }
}

/// <summary>
/// Outcome of evaluating one condition expression. <paramref name="Detail"/> is a
/// truncated, human-readable rendering of the resolved comparison for run events.
/// </summary>
public sealed record ConditionEvaluation(bool Result, string? Detail);
