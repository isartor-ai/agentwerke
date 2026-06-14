using System.Xml.Linq;

namespace Autofac.Workflows.Camunda;

public interface ICamunda8SpikeAnalyzer
{
    Camunda8SpikeReport Analyze(string bpmnXml);
}

public sealed class Camunda8SpikeAnalyzer : ICamunda8SpikeAnalyzer
{
    public Camunda8SpikeReport Analyze(string bpmnXml)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(bpmnXml);

        var document = XDocument.Parse(bpmnXml);
        var elementMappings = document
            .Descendants()
            .Where(static element => IsInterestingElement(element.Name.LocalName))
            .Select(MapElement)
            .ToArray();

        return new Camunda8SpikeReport(
            EngineId: "camunda8-spike",
            Summary: "Camunda 8 can cover Autofac's MVP slice by mapping service work to job workers, approvals to user tasks, timers to BPMN timer events, and inbound starts to message or webhook-based connectors.",
            ElementMappings: elementMappings,
            TriggerMappings:
            [
                new Camunda8TriggerMapping(
                    AutofacTrigger: "manual/api",
                    CamundaConstruct: "none start event",
                    SupportLevel: "supported",
                    Notes: "Autofac can start a process instance directly through the engine API for manual or API-driven launches."),
                new Camunda8TriggerMapping(
                    AutofacTrigger: "message",
                    CamundaConstruct: "message start event",
                    SupportLevel: "supported",
                    Notes: "Camunda 8 supports message start events that create a new process instance when a matching message is correlated."),
                new Camunda8TriggerMapping(
                    AutofacTrigger: "webhook",
                    CamundaConstruct: "HTTP webhook inbound connector",
                    SupportLevel: "supported",
                    Notes: "Camunda 8 inbound connectors expose webhook endpoints that can start a workflow from an external HTTP call."),
                new Camunda8TriggerMapping(
                    AutofacTrigger: "timer",
                    CamundaConstruct: "timer start event",
                    SupportLevel: "supported",
                    Notes: "Camunda 8 schedules timer start events on deployment and creates new process instances when the timer fires.")
            ],
            Notes:
            [
                "Service tasks should map Autofac agent actions to Camunda job types so the Agent Orchestrator can operate as a controlled worker.",
                "User approvals can remain human-governed through Camunda user tasks while Autofac keeps policy, audit, and approval metadata in its own model.",
                "Webhook-oriented starts should use inbound connectors, while message-driven starts can use native message start events."
            ]);
    }

    private static bool IsInterestingElement(string localName)
    {
        return localName is "startEvent" or "serviceTask" or "userTask" or "intermediateCatchEvent" or "boundaryEvent";
    }

    private static Camunda8ElementMapping MapElement(XElement element)
    {
        var id = element.Attribute("id")?.Value ?? string.Empty;
        var name = element.Attribute("name")?.Value;
        var type = element.Name.LocalName;

        return type switch
        {
            "serviceTask" => new Camunda8ElementMapping(
                ElementId: id,
                ElementName: name,
                SourceType: type,
                CamundaConstruct: "service task",
                ExecutionPattern: "job worker",
                SupportLevel: "supported",
                Notes:
                [
                    "Entering a service task creates a job that waits for a worker to complete it.",
                    "Autofac agent actions can be projected onto Camunda job types and handled by the Agent Orchestrator."
                ]),
            "userTask" => new Camunda8ElementMapping(
                ElementId: id,
                ElementName: name,
                SourceType: type,
                CamundaConstruct: "user task",
                ExecutionPattern: "engine-managed human task",
                SupportLevel: "supported",
                Notes:
                [
                    "Camunda 8 creates a user task instance and waits until it is completed.",
                    "Autofac can keep its richer approval metadata outside the engine while using the user task lifecycle for orchestration."
                ]),
            "intermediateCatchEvent" when HasChild(element, "timerEventDefinition") => new Camunda8ElementMapping(
                ElementId: id,
                ElementName: name,
                SourceType: type,
                CamundaConstruct: "intermediate timer catch event",
                ExecutionPattern: "scheduled wait",
                SupportLevel: "supported",
                Notes:
                [
                    "Camunda 8 schedules a timer when the catch event is entered and continues once the timer fires."
                ]),
            "startEvent" when HasChild(element, "timerEventDefinition") => new Camunda8ElementMapping(
                ElementId: id,
                ElementName: name,
                SourceType: type,
                CamundaConstruct: "timer start event",
                ExecutionPattern: "scheduled process start",
                SupportLevel: "supported",
                Notes:
                [
                    "Camunda 8 schedules timer start events when the process is deployed."
                ]),
            "startEvent" when HasChild(element, "messageEventDefinition") => new Camunda8ElementMapping(
                ElementId: id,
                ElementName: name,
                SourceType: type,
                CamundaConstruct: "message start event",
                ExecutionPattern: "message correlation",
                SupportLevel: "supported",
                Notes:
                [
                    "Camunda 8 creates a new process instance when a matching message is correlated to the start event."
                ]),
            "startEvent" => new Camunda8ElementMapping(
                ElementId: id,
                ElementName: name,
                SourceType: type,
                CamundaConstruct: "none start event",
                ExecutionPattern: "direct process start",
                SupportLevel: "supported",
                Notes:
                [
                    "Manual or API-triggered Autofac runs can map to a none start event plus engine start call."
                ]),
            "boundaryEvent" when HasChild(element, "timerEventDefinition") => new Camunda8ElementMapping(
                ElementId: id,
                ElementName: name,
                SourceType: type,
                CamundaConstruct: "timer boundary event",
                ExecutionPattern: "interrupting or non-interrupting timeout",
                SupportLevel: "supported",
                Notes:
                [
                    "Camunda 8 supports timer boundary events for timeout handling on an attached activity."
                ]),
            _ => new Camunda8ElementMapping(
                ElementId: id,
                ElementName: name,
                SourceType: type,
                CamundaConstruct: "manual review required",
                ExecutionPattern: "unsupported in spike",
                SupportLevel: "review",
                Notes:
                [
                    "This BPMN construct needs explicit review against Camunda 8 coverage before relying on it in production."
                ])
        };
    }

    private static bool HasChild(XElement element, string childName)
    {
        return element.Elements().Any(child => child.Name.LocalName == childName);
    }
}

public sealed record Camunda8SpikeReport(
    string EngineId,
    string Summary,
    IReadOnlyList<Camunda8ElementMapping> ElementMappings,
    IReadOnlyList<Camunda8TriggerMapping> TriggerMappings,
    IReadOnlyList<string> Notes);

public sealed record Camunda8ElementMapping(
    string ElementId,
    string? ElementName,
    string SourceType,
    string CamundaConstruct,
    string ExecutionPattern,
    string SupportLevel,
    IReadOnlyList<string> Notes);

public sealed record Camunda8TriggerMapping(
    string AutofacTrigger,
    string CamundaConstruct,
    string SupportLevel,
    string Notes);
