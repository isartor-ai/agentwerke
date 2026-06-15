namespace Autofac.Agents.Tools;

public interface IToolRegistry
{
    IAgentTool? Find(string toolName);

    IReadOnlyList<IAgentTool> All();
}

public sealed class ToolRegistry : IToolRegistry
{
    private readonly IReadOnlyDictionary<string, IAgentTool> _tools;
    private readonly IReadOnlyList<IAgentTool> _all;

    public ToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _all = tools.OrderBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase).ToArray();
        _tools = _all.ToDictionary(static tool => tool.Name, StringComparer.OrdinalIgnoreCase);
    }

    public IAgentTool? Find(string toolName) =>
        _tools.TryGetValue(toolName, out var tool) ? tool : null;

    public IReadOnlyList<IAgentTool> All() => _all;
}
