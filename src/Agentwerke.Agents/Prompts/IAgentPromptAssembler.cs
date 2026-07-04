namespace Agentwerke.Agents.Prompts;

public interface IAgentPromptAssembler
{
    AgentPromptAssemblyResult Assemble(AgentPromptAssemblyRequest request);
}
