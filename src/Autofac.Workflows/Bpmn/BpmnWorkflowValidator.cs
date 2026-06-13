using System.Xml;
using System.Xml.Linq;

namespace Autofac.Workflows.Bpmn;

public sealed class BpmnWorkflowValidator : IBpmnWorkflowValidator
{
    private static readonly HashSet<string> SupportedElementNames =
    [
        "startEvent",
        "endEvent",
        "serviceTask",
        "userTask",
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
        var autofacNamespace = document.Root?.GetNamespaceOfPrefix("autofac") ?? XNamespace.Get("https://autofac.dev/bpmn/extensions/v1");

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
        var candidates = process.Descendants().Where(element =>
            element.Name.Namespace == bpmnNamespace &&
            element.Attribute("id") is not null);

        foreach (var element in candidates)
        {
            var localName = element.Name.LocalName;
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

            AutofacTaskMetadata? metadata = null;
            AutofacApprovalMetadata? approvalMetadata = null;

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
                    metadata = ValidateAgentTaskMetadata(element, autofacNamespace, errors, warnings);
                    break;
                case "userTask":
                    approvalMetadata = ValidateApprovalMetadata(element, autofacNamespace, errors, warnings);
                    break;
                case "intermediateCatchEvent":
                    ValidateTimerEvent(element, errors);
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
                ApprovalMetadata: approvalMetadata));
        }

        var definition = new BpmnWorkflowDefinition(
            ProcessId: process.Attribute("id")?.Value ?? "unknown-process",
            ProcessName: process.Attribute("name")?.Value,
            Nodes: nodes);

        return new BpmnValidationResult(definition, errors, warnings);
    }

    private static AutofacTaskMetadata? ValidateAgentTaskMetadata(
        XElement element,
        XNamespace autofacNamespace,
        ICollection<BpmnValidationError> errors,
        ICollection<BpmnValidationWarning> warnings)
    {
        var extensionElements = GetExtensionElements(element);
        var agentTask = extensionElements?
            .Elements(autofacNamespace + "agentTask")
            .FirstOrDefault();

        if (agentTask is null)
        {
            errors.Add(CreateError(element,
                "Service/script task requires autofac:agentTask metadata under bpmn:extensionElements."));
            return null;
        }

        if (extensionElements?.Elements(autofacNamespace + "approvalTask").Any() == true)
        {
            warnings.Add(CreateWarning(
                element,
                "Service/script task contains autofac:approvalTask metadata that will be ignored. Use autofac:agentTask for executable tasks."));
        }

        WarnOnUnexpectedAutofacExtensionElements(
            extensionElements,
            autofacNamespace,
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
                $"autofac:agentTask is missing required attributes: {string.Join(", ", missingAttributes)}."));
            return null;
        }

        var requiresEvidence = (agentTask.Attribute("requiresEvidence")?.Value ?? string.Empty)
            .Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

        if (string.IsNullOrWhiteSpace(agentTask.Attribute("environment")?.Value))
        {
            warnings.Add(CreateWarning(
                element,
                "autofac:agentTask is missing optional 'environment' metadata. Add it to make execution context clearer."));
        }

        if (requiresEvidence.Length != requiresEvidence.Distinct(StringComparer.OrdinalIgnoreCase).Count())
        {
            warnings.Add(CreateWarning(
                element,
                "autofac:agentTask 'requiresEvidence' contains duplicate entries. Duplicates will be ignored at runtime."));
        }

        var maxRetries = ParseNonNegativeIntAttribute(agentTask, "maxRetries", element, errors);
        var retryBackoffSeconds = ParseNonNegativeIntAttribute(agentTask, "retryBackoffSeconds", element, errors);
        var failUntilAttempt = ParseNonNegativeIntAttribute(agentTask, "failUntilAttempt", element, errors);
        var timeoutSeconds = ParseNullableNonNegativeIntAttribute(agentTask, "timeoutSeconds", element, errors);
        var simulateTimeout = ParseBooleanAttribute(agentTask, "simulateTimeout", element, errors);

        if (maxRetries > 0 && retryBackoffSeconds == 0)
        {
            warnings.Add(CreateWarning(
                element,
                "autofac:agentTask enables retries without a retryBackoffSeconds value. Retries will happen immediately."));
        }

        if (simulateTimeout && timeoutSeconds is null)
        {
            warnings.Add(CreateWarning(
                element,
                "autofac:agentTask sets simulateTimeout='true' without timeoutSeconds. Timeout simulation may be hard to reason about."));
        }

        return new AutofacTaskMetadata(
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
            TimeoutSeconds: timeoutSeconds);
    }

    private static AutofacApprovalMetadata? ValidateApprovalMetadata(
        XElement element,
        XNamespace autofacNamespace,
        ICollection<BpmnValidationError> errors,
        ICollection<BpmnValidationWarning> warnings)
    {
        var extensionElements = GetExtensionElements(element);
        var approvalTask = extensionElements?
            .Elements(autofacNamespace + "approvalTask")
            .FirstOrDefault();

        if (approvalTask is null)
        {
            errors.Add(CreateError(element,
                "User task requires autofac:approvalTask metadata under bpmn:extensionElements."));
            return null;
        }

        if (extensionElements?.Elements(autofacNamespace + "agentTask").Any() == true)
        {
            warnings.Add(CreateWarning(
                element,
                "User task contains autofac:agentTask metadata that will be ignored. Use autofac:approvalTask for approval gates."));
        }

        WarnOnUnexpectedAutofacExtensionElements(
            extensionElements,
            autofacNamespace,
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
                $"autofac:approvalTask is missing required attributes: {string.Join(", ", missingAttributes)}."));
            return null;
        }

        return new AutofacApprovalMetadata(
            PurposeType: purposeType!,
            PolicyTag: policyTag!);
    }

    private static XElement? GetExtensionElements(XElement element)
    {
        return element.Elements()
            .FirstOrDefault(static child => child.Name.LocalName == "extensionElements");
    }

    private static void WarnOnUnexpectedAutofacExtensionElements(
        XElement? extensionElements,
        XNamespace autofacNamespace,
        XElement ownerElement,
        IReadOnlyCollection<string> allowedNames,
        ICollection<BpmnValidationWarning> warnings)
    {
        if (extensionElements is null)
        {
            return;
        }

        foreach (var extensionElement in extensionElements.Elements().Where(child => child.Name.Namespace == autofacNamespace))
        {
            if (allowedNames.Contains(extensionElement.Name.LocalName))
            {
                continue;
            }

            warnings.Add(CreateWarning(
                ownerElement,
                $"Unexpected Autofac extension element '{extensionElement.Name.LocalName}' will be ignored for this BPMN node type."));
        }
    }

    private static void ValidateTimerEvent(XElement element, ICollection<BpmnValidationError> errors)
    {
        var hasTimerDefinition = element.Elements().Any(static child => child.Name.LocalName == "timerEventDefinition");
        if (!hasTimerDefinition)
        {
            errors.Add(CreateError(element,
                "Intermediate catch event must define bpmn:timerEventDefinition for timer handling."));
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
                $"autofac:agentTask attribute '{attributeName}' must be a non-negative integer."));
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
                $"autofac:agentTask attribute '{attributeName}' must be a non-negative integer when specified."));
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
                $"autofac:agentTask attribute '{attributeName}' must be 'true' or 'false'."));
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
