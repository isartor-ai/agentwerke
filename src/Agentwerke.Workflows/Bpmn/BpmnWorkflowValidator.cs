using System.Xml;
using System.Xml.Linq;
using Agentwerke.Domain.AgentRuntime;

namespace Agentwerke.Workflows.Bpmn;

public sealed class BpmnWorkflowValidator : IBpmnWorkflowValidator
{
    private static readonly HashSet<string> SupportedElementNames =
    [
        "startEvent",
        "endEvent",
        "serviceTask",
        "userTask",
        "receiveTask",
        "scriptTask",
        "exclusiveGateway",
        "parallelGateway",
        "boundaryEvent",
        "intermediateCatchEvent",
        "subProcess"
    ];

    private static readonly HashSet<string> SupportedEventDefinitions =
    [
        "timerEventDefinition",
        "errorEventDefinition",
        "escalationEventDefinition"
    ];

    public BpmnValidationResult Validate(string bpmnXml)
    {
        if (string.IsNullOrWhiteSpace(bpmnXml))
        {
            return new BpmnValidationResult(
                definition: null,
                errors:
                [
                    new BpmnValidationError(
                        "BPMN XML payload is empty.",
                        ElementId: null,
                        ElementName: "document",
                        LineNumber: null,
                        LinePosition: null)
                ],
                warnings: Array.Empty<BpmnValidationWarning>());
        }

        XDocument document;
        try
        {
            document = XDocument.Parse(bpmnXml, LoadOptions.SetLineInfo);
        }
        catch (XmlException ex)
        {
            return new BpmnValidationResult(
                definition: null,
                errors:
                [
                    new BpmnValidationError(
                        $"Invalid XML: {ex.Message}",
                        ElementId: null,
                        ElementName: "document",
                        LineNumber: ex.LineNumber,
                        LinePosition: ex.LinePosition)
                ],
                warnings: Array.Empty<BpmnValidationWarning>());
        }

        var errors = new List<BpmnValidationError>();
        var warnings = new List<BpmnValidationWarning>();
        var bpmnNamespace = document.Root?.GetNamespaceOfPrefix("bpmn") ?? XNamespace.Get("http://www.omg.org/spec/BPMN/20100524/MODEL");
        var agentwerkeNamespace = document.Root?.GetNamespaceOfPrefix("agentwerke") ?? XNamespace.Get("https://agentwerke.de/bpmn/extensions/v1");

        var process = document.Descendants(bpmnNamespace + "process").FirstOrDefault();
        if (process is null)
        {
            errors.Add(new BpmnValidationError(
                "BPMN document must contain at least one bpmn:process element.",
                ElementId: null,
                ElementName: "process",
                LineNumber: null,
                LinePosition: null));

            return new BpmnValidationResult(definition: null, errors, warnings);
        }

        if (string.IsNullOrWhiteSpace(process.Attribute("name")?.Value))
        {
            warnings.Add(CreateWarning(
                process,
                "Workflow process is missing a human-readable 'name' attribute. The UI will fall back to the process id."));
        }

        var nodes = new List<BpmnNodeDefinition>();
        var sequenceFlows = new List<BpmnSequenceFlow>();

        var candidates = process.Descendants().Where(element =>
            element.Name.Namespace == bpmnNamespace &&
            element.Attribute("id") is not null);

        foreach (var element in candidates)
        {
            var localName = element.Name.LocalName;

            if (localName == "sequenceFlow")
            {
                var sfId = element.Attribute("id")?.Value ?? string.Empty;
                var src = element.Attribute("sourceRef")?.Value ?? string.Empty;
                var tgt = element.Attribute("targetRef")?.Value ?? string.Empty;
                var conditionEl = element.Elements()
                    .FirstOrDefault(static c => c.Name.LocalName == "conditionExpression");
                var condition = conditionEl?.Value?.Trim();
                sequenceFlows.Add(new BpmnSequenceFlow(sfId, src, tgt, string.IsNullOrEmpty(condition) ? null : condition));
                continue;
            }

            if (!SupportedElementNames.Contains(localName))
            {
                if (element.Name.Namespace == bpmnNamespace && element.Attribute("id") is not null)
                {
                    errors.Add(CreateError(
                        element,
                        $"Unsupported BPMN element '{localName}'. Supported elements: {string.Join(", ", SupportedElementNames.OrderBy(static n => n))}."));
                }

                continue;
            }

            var id = element.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(id))
            {
                errors.Add(CreateError(element, $"Element '{localName}' is missing required 'id' attribute."));
                continue;
            }

            AgentwerkeTaskMetadata? metadata = null;
            AgentwerkeApprovalMetadata? approvalMetadata = null;
            string? timerDuration = null;
            AgentwerkeExternalEventMetadata? externalEventMetadata = null;

            if ((localName is "serviceTask" or "scriptTask" or "userTask") &&
                string.IsNullOrWhiteSpace(element.Attribute("name")?.Value))
            {
                warnings.Add(CreateWarning(
                    element,
                    $"'{localName}' should define a descriptive 'name' attribute for clearer designer and run views."));
            }

            switch (localName)
            {
                case "serviceTask":
                case "scriptTask":
                    metadata = ValidateAgentTaskMetadata(element, agentwerkeNamespace, errors, warnings);
                    break;
                case "userTask":
                    approvalMetadata = ValidateApprovalMetadata(element, agentwerkeNamespace, errors, warnings);
                    break;
                case "receiveTask":
                    externalEventMetadata = ValidateExternalEventMetadata(element, agentwerkeNamespace, errors, warnings);
                    break;
                case "intermediateCatchEvent":
                    if (HasChild(element, "timerEventDefinition"))
                    {
                        timerDuration = ParseTimerDuration(element, errors);
                    }
                    else if (HasChild(element, "messageEventDefinition"))
                    {
                        externalEventMetadata = ValidateExternalEventMetadata(element, agentwerkeNamespace, errors, warnings);
                    }
                    else
                    {
                        errors.Add(CreateError(
                            element,
                            "Intermediate catch event must define either bpmn:timerEventDefinition or bpmn:messageEventDefinition."));
                    }
                    break;
                case "boundaryEvent":
                    ValidateBoundaryEvent(element, errors);
                    break;
            }

            nodes.Add(new BpmnNodeDefinition(
                Id: id,
                Name: element.Attribute("name")?.Value,
                ElementName: localName,
                Metadata: metadata,
                ApprovalMetadata: approvalMetadata,
                TimerDuration: timerDuration,
                ExternalEventMetadata: externalEventMetadata));
        }

        ValidateSequenceFlows(nodes, sequenceFlows, errors);

        var definition = new BpmnWorkflowDefinition(
            ProcessId: process.Attribute("id")?.Value ?? "unknown-process",
            ProcessName: process.Attribute("name")?.Value,
            Nodes: nodes,
            SequenceFlows: sequenceFlows.Count > 0 ? sequenceFlows : null);

        return new BpmnValidationResult(definition, errors, warnings);
    }

    private static AgentwerkeTaskMetadata? ValidateAgentTaskMetadata(
        XElement element,
        XNamespace agentwerkeNamespace,
        ICollection<BpmnValidationError> errors,
        ICollection<BpmnValidationWarning> warnings)
    {
        var extensionElements = GetExtensionElements(element);
        var agentTask = extensionElements?
            .Elements(agentwerkeNamespace + "agentTask")
            .FirstOrDefault();

        if (agentTask is null)
        {
            errors.Add(CreateError(element,
                "Service/script task requires agentwerke:agentTask metadata under bpmn:extensionElements."));
            return null;
        }

        if (extensionElements?.Elements(agentwerkeNamespace + "approvalTask").Any() == true)
        {
            warnings.Add(CreateWarning(
                element,
                "Service/script task contains agentwerke:approvalTask metadata that will be ignored. Use agentwerke:agentTask for executable tasks."));
        }

        WarnOnUnexpectedAgentwerkeExtensionElements(
            extensionElements,
            agentwerkeNamespace,
            element,
            ["agentTask"],
            warnings);

        var missingAttributes = new List<string>();

        var agent = GetAttribute(agentTask, "agent", missingAttributes);
        var action = GetAttribute(agentTask, "action", missingAttributes);
        var purposeType = GetAttribute(agentTask, "purposeType", missingAttributes);
        var policyTag = GetAttribute(agentTask, "policyTag", missingAttributes);

        if (missingAttributes.Count > 0)
        {
            errors.Add(CreateError(
                element,
                $"agentwerke:agentTask is missing required attributes: {string.Join(", ", missingAttributes)}."));
            return null;
        }

        var requiresEvidence = (agentTask.Attribute("requiresEvidence")?.Value ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (string.IsNullOrWhiteSpace(agentTask.Attribute("environment")?.Value))
        {
            warnings.Add(CreateWarning(
                element,
                "agentwerke:agentTask is missing optional 'environment' metadata. Add it to make execution context clearer."));
        }

        if (requiresEvidence.Length != requiresEvidence.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            warnings.Add(CreateWarning(
                element,
                "agentwerke:agentTask 'requiresEvidence' contains duplicate entries. Duplicates will be ignored at runtime."));
        }

        var maxRetries = ParseNonNegativeIntAttribute(agentTask, "maxRetries", element, errors);
        var retryBackoffSeconds = ParseNonNegativeIntAttribute(agentTask, "retryBackoffSeconds", element, errors);
        var failUntilAttempt = ParseNonNegativeIntAttribute(agentTask, "failUntilAttempt", element, errors);
        var timeoutSeconds = ParseNullableNonNegativeIntAttribute(agentTask, "timeoutSeconds", element, errors);
        var simulateTimeout = ParseBooleanAttribute(agentTask, "simulateTimeout", element, errors);
        var executionMode = ParseExecutionModeAttribute(agentTask, element, errors);

        if (maxRetries > 0 && retryBackoffSeconds == 0)
        {
            warnings.Add(CreateWarning(
                element,
                "agentwerke:agentTask enables retries without a retryBackoffSeconds value. Retries will happen immediately."));
        }

        if (simulateTimeout && timeoutSeconds is null)
        {
            warnings.Add(CreateWarning(
                element,
                "agentwerke:agentTask sets simulateTimeout='true' without timeoutSeconds. Timeout simulation may be hard to reason about."));
        }

        var runtimeContract = ParseRuntimeContract(agentTask, agentwerkeNamespace, element, errors);

        return new AgentwerkeTaskMetadata(
            Agent: agent!,
            Action: action!,
            Environment: agentTask.Attribute("environment")?.Value,
            PurposeType: purposeType!,
            PolicyTag: policyTag!,
            RequiresEvidence: requiresEvidence,
            MaxRetries: maxRetries,
            RetryBackoffSeconds: retryBackoffSeconds,
            FailUntilAttempt: failUntilAttempt,
            SimulateTimeout: simulateTimeout,
            TimeoutSeconds: timeoutSeconds,
            RuntimeContract: runtimeContract,
            ExecutionMode: executionMode,
            SandboxProfile: agentTask.Attribute("sandboxProfile")?.Value);
    }

    private static AgentRuntimeContract? ParseRuntimeContract(
        XElement agentTask,
        XNamespace agentwerkeNamespace,
        XElement element,
        ICollection<BpmnValidationError> errors)
    {
        var permissionLevel = agentTask.Attribute("permissionLevel")?.Value;
        var allowedTools = ParseCsvAttribute(agentTask, "allowedTools");
        var deniedTools = ParseCsvAttribute(agentTask, "deniedTools");
        var prompt = ParsePromptContract(agentTask, agentwerkeNamespace);

        if (prompt is null
            && string.IsNullOrWhiteSpace(permissionLevel)
            && allowedTools.Count == 0
            && deniedTools.Count == 0)
        {
            return null;
        }

        var normalizedPermissionLevel = string.IsNullOrWhiteSpace(permissionLevel)
            ? AgentPermissionLevels.ReadOnly
            : permissionLevel.Trim();

        if (!IsKnownPermissionLevel(normalizedPermissionLevel))
        {
            errors.Add(CreateError(
                element,
                "agentwerke:agentTask permissionLevel must be one of: read-only, read-write, full."));
            return null;
        }

        return new AgentRuntimeContract
        {
            Prompt = prompt,
            Permissions = new AgentPermissionContract
            {
                Level = normalizedPermissionLevel,
                AllowedTools = allowedTools,
                DeniedTools = deniedTools
            }
        };
    }

    /// <summary>
    /// Parses a per-task prompt for an agentTask (#149). Supports, in priority order:
    /// a child <c>&lt;agentwerke:prompt&gt;…&lt;/agentwerke:prompt&gt;</c> element (best for
    /// multi-line text), the <c>prompt</c> attribute (inline), and the <c>promptFile</c>
    /// attribute (a prompt-template file path). The prompt may contain <c>{{input.*}}</c>,
    /// <c>{{output.*}}</c>, and other run-context placeholders, which the prompt assembler
    /// renders at execution time. Returns null when no prompt is declared.
    /// </summary>
    private static AgentPromptContract? ParsePromptContract(XElement agentTask, XNamespace agentwerkeNamespace)
    {
        var promptFile = agentTask.Attribute("promptFile")?.Value;

        var inlineElement = agentTask.Element(agentwerkeNamespace + "prompt")?.Value;
        var inline = !string.IsNullOrWhiteSpace(inlineElement)
            ? inlineElement.Trim()
            : agentTask.Attribute("prompt")?.Value?.Trim();

        if (string.IsNullOrWhiteSpace(inline) && string.IsNullOrWhiteSpace(promptFile))
        {
            return null;
        }

        return new AgentPromptContract
        {
            Inline = string.IsNullOrWhiteSpace(inline) ? null : inline,
            File = string.IsNullOrWhiteSpace(promptFile) ? null : promptFile.Trim()
        };
    }

    private static string? ParseExecutionModeAttribute(
        XElement agentTask,
        XElement element,
        ICollection<BpmnValidationError> errors)
    {
        var executionMode = agentTask.Attribute("executionMode")?.Value;
        if (string.IsNullOrWhiteSpace(executionMode))
        {
            return null;
        }

        if (!AgentExecutionModes.All.Contains(executionMode, StringComparer.OrdinalIgnoreCase))
        {
            errors.Add(CreateError(
                element,
                $"executionMode must be one of: {string.Join(", ", AgentExecutionModes.All)}."));
            return null;
        }

        return executionMode;
    }

    private static IReadOnlyList<string> ParseCsvAttribute(XElement element, string attributeName) =>
        (element.Attribute(attributeName)?.Value ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool IsKnownPermissionLevel(string value) =>
        string.Equals(value, AgentPermissionLevels.ReadOnly, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, AgentPermissionLevels.ReadWrite, StringComparison.OrdinalIgnoreCase) ||
        string.Equals(value, AgentPermissionLevels.Full, StringComparison.OrdinalIgnoreCase);

    private static AgentwerkeApprovalMetadata? ValidateApprovalMetadata(
        XElement element,
        XNamespace agentwerkeNamespace,
        ICollection<BpmnValidationError> errors,
        ICollection<BpmnValidationWarning> warnings)
    {
        var extensionElements = GetExtensionElements(element);
        var approvalTask = extensionElements?
            .Elements(agentwerkeNamespace + "approvalTask")
            .FirstOrDefault();

        if (approvalTask is null)
        {
            errors.Add(CreateError(element,
                "User task requires agentwerke:approvalTask metadata under bpmn:extensionElements."));
            return null;
        }

        if (extensionElements?.Elements(agentwerkeNamespace + "agentTask").Any() == true)
        {
            warnings.Add(CreateWarning(
                element,
                "User task contains agentwerke:agentTask metadata that will be ignored. Use agentwerke:approvalTask for approval gates."));
        }

        WarnOnUnexpectedAgentwerkeExtensionElements(
            extensionElements,
            agentwerkeNamespace,
            element,
            ["approvalTask"],
            warnings);

        var missingAttributes = new List<string>();
        var purposeType = GetAttribute(approvalTask, "purposeType", missingAttributes);
        var policyTag = GetAttribute(approvalTask, "policyTag", missingAttributes);

        if (missingAttributes.Count > 0)
        {
            errors.Add(CreateError(
                element,
                $"agentwerke:approvalTask is missing required attributes: {string.Join(", ", missingAttributes)}."));
            return null;
        }

        return new AgentwerkeApprovalMetadata(
            PurposeType: purposeType!,
            PolicyTag: policyTag!);
    }

    private static AgentwerkeExternalEventMetadata? ValidateExternalEventMetadata(
        XElement element,
        XNamespace agentwerkeNamespace,
        ICollection<BpmnValidationError> errors,
        ICollection<BpmnValidationWarning> warnings)
    {
        var extensionElements = GetExtensionElements(element);
        var externalEvent = extensionElements?
            .Elements(agentwerkeNamespace + "externalEvent")
            .FirstOrDefault();

        if (externalEvent is null)
        {
            errors.Add(CreateError(
                element,
                $"{element.Name.LocalName} requires agentwerke:externalEvent metadata under bpmn:extensionElements."));
            return null;
        }

        WarnOnUnexpectedAgentwerkeExtensionElements(
            extensionElements,
            agentwerkeNamespace,
            element,
            ["externalEvent"],
            warnings);

        var missingAttributes = new List<string>();
        var messageName = GetAttribute(externalEvent, "messageName", missingAttributes);
        var correlationKeyTemplate = GetAttribute(externalEvent, "correlationKeyTemplate", missingAttributes);

        if (missingAttributes.Count > 0)
        {
            errors.Add(CreateError(
                element,
                $"agentwerke:externalEvent is missing required attributes: {string.Join(", ", missingAttributes)}."));
            return null;
        }

        return new AgentwerkeExternalEventMetadata(
            MessageName: messageName!,
            CorrelationKeyTemplate: correlationKeyTemplate!);
    }

    private static XElement? GetExtensionElements(XElement element)
    {
        return element.Elements()
            .FirstOrDefault(static child => child.Name.LocalName == "extensionElements");
    }

    private static bool HasChild(XElement element, string localName) =>
        element.Elements().Any(child => child.Name.LocalName == localName);

    private static void WarnOnUnexpectedAgentwerkeExtensionElements(
        XElement? extensionElements,
        XNamespace agentwerkeNamespace,
        XElement ownerElement,
        IReadOnlyCollection<string> allowedNames,
        ICollection<BpmnValidationWarning> warnings)
    {
        if (extensionElements is null)
        {
            return;
        }

        foreach (var extensionElement in extensionElements.Elements().Where(child => child.Name.Namespace == agentwerkeNamespace))
        {
            if (allowedNames.Contains(extensionElement.Name.LocalName))
            {
                continue;
            }

            warnings.Add(CreateWarning(
                ownerElement,
                $"Unexpected Agentwerke extension element '{extensionElement.Name.LocalName}' will be ignored for this BPMN node type."));
        }
    }

    private static string? ParseTimerDuration(XElement element, ICollection<BpmnValidationError> errors)
    {
        var timerDef = element.Elements().FirstOrDefault(static c => c.Name.LocalName == "timerEventDefinition");
        if (timerDef is null)
        {
            errors.Add(CreateError(element,
                "Intermediate catch event must define bpmn:timerEventDefinition for timer handling."));
            return null;
        }

        var timeDuration = timerDef.Elements().FirstOrDefault(static c => c.Name.LocalName == "timeDuration");
        return timeDuration?.Value?.Trim();
    }

    private static void ValidateSequenceFlows(
        IReadOnlyList<BpmnNodeDefinition> nodes,
        IReadOnlyList<BpmnSequenceFlow> sequenceFlows,
        ICollection<BpmnValidationError> errors)
    {
        if (sequenceFlows.Count == 0)
            return;

        var nodeIds = nodes.Select(static n => n.Id).ToHashSet(StringComparer.Ordinal);

        foreach (var flow in sequenceFlows)
        {
            if (!string.IsNullOrEmpty(flow.SourceRef) && !nodeIds.Contains(flow.SourceRef))
            {
                errors.Add(new BpmnValidationError(
                    $"Sequence flow '{flow.Id}' references unknown source node '{flow.SourceRef}'.",
                    ElementId: flow.Id, ElementName: "sequenceFlow",
                    LineNumber: null, LinePosition: null));
            }

            if (!string.IsNullOrEmpty(flow.TargetRef) && !nodeIds.Contains(flow.TargetRef))
            {
                errors.Add(new BpmnValidationError(
                    $"Sequence flow '{flow.Id}' references unknown target node '{flow.TargetRef}'.",
                    ElementId: flow.Id, ElementName: "sequenceFlow",
                    LineNumber: null, LinePosition: null));
            }
        }
    }

    private static void ValidateBoundaryEvent(XElement element, ICollection<BpmnValidationError> errors)
    {
        var hasSupportedDefinition = element
            .Elements()
            .Any(child => SupportedEventDefinitions.Contains(child.Name.LocalName));

        if (!hasSupportedDefinition)
        {
            errors.Add(CreateError(element,
                "Boundary event must define one of: timerEventDefinition, errorEventDefinition, escalationEventDefinition."));
        }
    }

    private static string? GetAttribute(XElement element, string attributeName, ICollection<string> missingAttributes)
    {
        var value = element.Attribute(attributeName)?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            missingAttributes.Add(attributeName);
            return null;
        }

        return value;
    }

    private static int ParseNonNegativeIntAttribute(
        XElement source,
        string attributeName,
        XElement ownerElement,
        ICollection<BpmnValidationError> errors)
    {
        var value = source.Attribute(attributeName)?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return 0;
        }

        if (!int.TryParse(value, out var parsed) || parsed < 0)
        {
            errors.Add(CreateError(ownerElement,
                $"agentwerke:agentTask attribute '{attributeName}' must be a non-negative integer."));
            return 0;
        }

        return parsed;
    }

    private static int? ParseNullableNonNegativeIntAttribute(
        XElement source,
        string attributeName,
        XElement ownerElement,
        ICollection<BpmnValidationError> errors)
    {
        var value = source.Attribute(attributeName)?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        if (!int.TryParse(value, out var parsed) || parsed < 0)
        {
            errors.Add(CreateError(ownerElement,
                $"agentwerke:agentTask attribute '{attributeName}' must be a non-negative integer when specified."));
            return null;
        }

        return parsed;
    }

    private static bool ParseBooleanAttribute(
        XElement source,
        string attributeName,
        XElement ownerElement,
        ICollection<BpmnValidationError> errors)
    {
        var value = source.Attribute(attributeName)?.Value;
        if (string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        if (!bool.TryParse(value, out var parsed))
        {
            errors.Add(CreateError(ownerElement,
                $"agentwerke:agentTask attribute '{attributeName}' must be 'true' or 'false'."));
            return false;
        }

        return parsed;
    }

    private static BpmnValidationError CreateError(XElement element, string message)
    {
        var lineInfo = (IXmlLineInfo)element;
        return new BpmnValidationError(
            Message: message,
            ElementId: element.Attribute("id")?.Value,
            ElementName: element.Name.LocalName,
            LineNumber: lineInfo.HasLineInfo() ? lineInfo.LineNumber : null,
            LinePosition: lineInfo.HasLineInfo() ? lineInfo.LinePosition : null);
    }

    private static BpmnValidationWarning CreateWarning(XElement element, string message)
    {
        var lineInfo = (IXmlLineInfo)element;
        return new BpmnValidationWarning(
            Message: message,
            ElementId: element.Attribute("id")?.Value,
            ElementName: element.Name.LocalName,
            LineNumber: lineInfo.HasLineInfo() ? lineInfo.LineNumber : null,
            LinePosition: lineInfo.HasLineInfo() ? lineInfo.LinePosition : null);
    }
}
