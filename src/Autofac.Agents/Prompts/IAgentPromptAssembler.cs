namespace Autofac.Agents.Prompts;

public interface IAgentPromptAssembler
{
    AgentPromptAssemblyResult Assemble(AgentPromptAssemblyRequest request);
}
