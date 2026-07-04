namespace Agentwerke.Agents.Tools;

public interface IToolRegistry
{
    IAgentTool? Find(string toolName);

    IReadOnlyList<IAgentTool> All();

    void Register(IAgentTool tool);

    void RegisterRange(IEnumerable<IAgentTool> tools);
}

public sealed class ToolRegistry : IToolRegistry
{
    private readonly Dictionary<string, IAgentTool> _tools;

    public ToolRegistry(IEnumerable<IAgentTool> tools)
    {
        _tools = new Dictionary<string, IAgentTool>(StringComparer.OrdinalIgnoreCase);
        RegisterRange(tools);
    }

    public IAgentTool? Find(string toolName) =>
        _tools.TryGetValue(toolName, out var tool) ? tool : null;

    public IReadOnlyList<IAgentTool> All() =>
        _tools.Values
            .OrderBy(static tool => tool.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    public void Register(IAgentTool tool)
    {
        ArgumentNullException.ThrowIfNull(tool);
        _tools[tool.Name] = tool;
    }

    public void RegisterRange(IEnumerable<IAgentTool> tools)
    {
        ArgumentNullException.ThrowIfNull(tools);

        foreach (var tool in tools)
        {
            Register(tool);
        }
    }
}
