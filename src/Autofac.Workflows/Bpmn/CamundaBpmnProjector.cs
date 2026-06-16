using System.Xml;
using System.Xml.Linq;

namespace Autofac.Workflows.Bpmn;

public sealed class CamundaBpmnProjector : ICamundaBpmnProjector
{
    private const string AutofacAgentJobType = "autofac.agent";

    private static readonly XNamespace BpmnNamespace = "http://www.omg.org/spec/BPMN/20100524/MODEL";
    private static readonly XNamespace AutofacNamespace = "https://autofac.ai/bpmn";
    private static readonly XNamespace ZeebeNamespace = "http://camunda.org/schema/zeebe/1.0";

    private readonly IBpmnWorkflowValidator _validator;

    public CamundaBpmnProjector(IBpmnWorkflowValidator validator)
    {
        _validator = validator;
    }

    public CamundaBpmnProjectionResult Project(string bpmnXml)
    {
        var validation = _validator.Validate(bpmnXml);
        if (!validation.IsValid || validation.Definition is null)
        {
            return new CamundaBpmnProjectionResult(
                validation.Definition,
                projectedBpmnXml: null,
                validation.Errors,
                validation.Warnings,
                bindings: []);
        }

        var document = XDocument.Parse(bpmnXml, LoadOptions.SetLineInfo);
        var root = document.Root;
        if (root is null)
        {
            return new CamundaBpmnProjectionResult(
                validation.Definition,
                projectedBpmnXml: null,
                errors:
                [
                    new BpmnValidationError(
                        "BPMN XML payload is empty.",
                        ElementId: null,
                        ElementName: "document",
                        LineNumber: null,
                        LinePosition: null)
                ],
                warnings: validation.Warnings,
                bindings: []);
        }

        var bpmnNamespace = root.GetNamespaceOfPrefix("bpmn") ?? BpmnNamespace;
        var autofacNamespace = root.GetNamespaceOfPrefix("autofac") ?? AutofacNamespace;
        var process = document.Descendants(bpmnNamespace + "process").FirstOrDefault();
        if (process is null)
        {
            return new CamundaBpmnProjectionResult(
                validation.Definition,
                projectedBpmnXml: null,
                errors:
                [
                    new BpmnValidationError(
                        "BPMN document must contain at least one bpmn:process element.",
                        ElementId: null,
                        ElementName: "process",
                        LineNumber: null,
                        LinePosition: null)
                ],
                warnings: validation.Warnings,
                bindings: []);
        }

        root.SetAttributeValue(XNamespace.Xmlns + "zeebe", ZeebeNamespace.NamespaceName);

        var projectionErrors = new List<BpmnValidationError>();
        var bindings = new List<CamundaTaskBinding>();
        var nodesById = validation.Definition.Nodes.ToDictionary(node => node.Id, StringComparer.Ordinal);

        foreach (var element in process.Descendants().Where(candidate =>
                     candidate.Name.Namespace == bpmnNamespace &&
                     candidate.Attribute("id") is not null))
        {
            var elementId = element.Attribute("id")?.Value;
            if (string.IsNullOrWhiteSpace(elementId) ||
                !nodesById.TryGetValue(elementId, out var node))
            {
                continue;
            }

            var localName = element.Name.LocalName;
            var autofacMetadata = GetExtensionElements(element, bpmnNamespace)?
                .Elements()
                .Where(child => child.Name.Namespace == autofacNamespace)
                .ToList() ?? [];

            if (autofacMetadata.Count > 0 &&
                localName is not ("serviceTask" or "scriptTask" or "userTask"))
            {
                projectionErrors.Add(CreateError(
                    element,
                    $"Autofac metadata is only supported on serviceTask, scriptTask, and userTask elements for Camunda projection. Found: {string.Join(", ", autofacMetadata.Select(metadata => metadata.Name.LocalName).Distinct(StringComparer.Ordinal))}."));
                continue;
            }

            if (node.Metadata is not null && localName is "serviceTask" or "scriptTask")
            {
                ProjectAgentTask(element, node.Metadata, bpmnNamespace, autofacNamespace, bindings);
                continue;
            }

            if (node.ApprovalMetadata is not null && localName == "userTask")
            {
                ProjectApprovalTask(element, node.ApprovalMetadata, bpmnNamespace, autofacNamespace, bindings);
            }
        }

        if (projectionErrors.Count > 0)
        {
            var combinedErrors = validation.Errors.Concat(projectionErrors).ToArray();
            return new CamundaBpmnProjectionResult(
                validation.Definition,
                projectedBpmnXml: null,
                errors: combinedErrors,
                warnings: validation.Warnings,
                bindings: []);
        }

        RemoveUnusedAutofacNamespace(root, autofacNamespace);

        return new CamundaBpmnProjectionResult(
            validation.Definition,
            document.ToString(SaveOptions.DisableFormatting),
            validation.Errors,
            validation.Warnings,
            bindings);
    }

    private static void ProjectAgentTask(
        XElement element,
        AutofacTaskMetadata metadata,
        XNamespace bpmnNamespace,
        XNamespace autofacNamespace,
        ICollection<CamundaTaskBinding> bindings)
    {
        if (element.Name.LocalName == "scriptTask")
        {
            element.Name = bpmnNamespace + "serviceTask";
        }

        var extensionElements = EnsureExtensionElements(element, bpmnNamespace);
        var agentTaskElement = extensionElements.Element(autofacNamespace + "agentTask");
        var hasRetriesOverride = !string.IsNullOrWhiteSpace(agentTaskElement?.Attribute("maxRetries")?.Value);
        RemoveAutofacMetadata(extensionElements, autofacNamespace);
        extensionElements.Elements(ZeebeNamespace + "taskDefinition").Remove();
        extensionElements.Elements(ZeebeNamespace + "taskHeaders").Remove();

        var headers = BuildAgentHeaders(element.Attribute("id")?.Value ?? string.Empty, metadata);
        var taskDefinition = new XElement(
            ZeebeNamespace + "taskDefinition",
            new XAttribute("type", AutofacAgentJobType));

        if (hasRetriesOverride)
        {
            taskDefinition.SetAttributeValue("retries", metadata.MaxRetries);
        }

        var taskHeaders = new XElement(
            ZeebeNamespace + "taskHeaders",
            headers.Select(header => new XElement(
                ZeebeNamespace + "header",
                new XAttribute("key", header.Key),
                new XAttribute("value", header.Value))));

        extensionElements.Add(taskDefinition);
        extensionElements.Add(taskHeaders);

        bindings.Add(new CamundaTaskBinding(
            ElementId: element.Attribute("id")?.Value ?? string.Empty,
            ElementName: element.Name.LocalName,
            JobType: AutofacAgentJobType,
            TaskHeaders: headers,
            AgentMetadata: metadata));
    }

    private static void ProjectApprovalTask(
        XElement element,
        AutofacApprovalMetadata metadata,
        XNamespace bpmnNamespace,
        XNamespace autofacNamespace,
        ICollection<CamundaTaskBinding> bindings)
    {
        var extensionElements = EnsureExtensionElements(element, bpmnNamespace);
        RemoveAutofacMetadata(extensionElements, autofacNamespace);
        extensionElements.Elements(ZeebeNamespace + "taskDefinition").Remove();
        extensionElements.Elements(ZeebeNamespace + "taskHeaders").Remove();

        var existingUserTask = extensionElements.Elements(ZeebeNamespace + "userTask").ToList();
        if (existingUserTask.Count == 0)
        {
            extensionElements.Add(new XElement(ZeebeNamespace + "userTask"));
        }
        else
        {
            foreach (var duplicate in existingUserTask.Skip(1))
            {
                duplicate.Remove();
            }
        }

        bindings.Add(new CamundaTaskBinding(
            ElementId: element.Attribute("id")?.Value ?? string.Empty,
            ElementName: element.Name.LocalName,
            JobType: null,
            TaskHeaders: new Dictionary<string, string>(StringComparer.Ordinal),
            ApprovalMetadata: metadata));
    }

    private static Dictionary<string, string> BuildAgentHeaders(string elementId, AutofacTaskMetadata metadata)
    {
        var headers = new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["autofac.elementId"] = elementId,
            ["autofac.agent"] = metadata.Agent,
            ["autofac.action"] = metadata.Action,
            ["autofac.purposeType"] = metadata.PurposeType,
            ["autofac.policyTag"] = metadata.PolicyTag,
            ["autofac.requiresEvidence"] = string.Join(",", metadata.RequiresEvidence)
        };

        if (!string.IsNullOrWhiteSpace(metadata.Environment))
        {
            headers["autofac.environment"] = metadata.Environment;
        }

        if (metadata.RetryBackoffSeconds > 0)
        {
            headers["autofac.retryBackoffSeconds"] = metadata.RetryBackoffSeconds.ToString();
        }

        if (metadata.FailUntilAttempt > 0)
        {
            headers["autofac.failUntilAttempt"] = metadata.FailUntilAttempt.ToString();
        }

        if (metadata.SimulateTimeout)
        {
            headers["autofac.simulateTimeout"] = bool.TrueString.ToLowerInvariant();
        }

        if (metadata.TimeoutSeconds is not null)
        {
            headers["autofac.timeoutSeconds"] = metadata.TimeoutSeconds.Value.ToString();
        }

        return headers;
    }

    private static XElement EnsureExtensionElements(XElement element, XNamespace bpmnNamespace)
    {
        var extensionElements = GetExtensionElements(element, bpmnNamespace);
        if (extensionElements is not null)
        {
            return extensionElements;
        }

        extensionElements = new XElement(bpmnNamespace + "extensionElements");
        var firstChild = element.Elements().FirstOrDefault();
        if (firstChild is null)
        {
            element.Add(extensionElements);
        }
        else
        {
            firstChild.AddBeforeSelf(extensionElements);
        }

        return extensionElements;
    }

    private static XElement? GetExtensionElements(XElement element, XNamespace bpmnNamespace)
    {
        return element.Element(bpmnNamespace + "extensionElements");
    }

    private static void RemoveAutofacMetadata(XElement extensionElements, XNamespace autofacNamespace)
    {
        extensionElements.Elements()
            .Where(child => child.Name.Namespace == autofacNamespace)
            .Remove();
    }

    private static void RemoveUnusedAutofacNamespace(XElement root, XNamespace autofacNamespace)
    {
        var usesAutofacNamespace = root
            .DescendantsAndSelf()
            .Any(element => element.Name.Namespace == autofacNamespace ||
                            element.Attributes().Any(attribute => attribute.Name.Namespace == autofacNamespace));

        if (usesAutofacNamespace)
        {
            return;
        }

        root.Attributes()
            .Where(attribute => attribute.IsNamespaceDeclaration &&
                                string.Equals(attribute.Value, autofacNamespace.NamespaceName, StringComparison.Ordinal))
            .Remove();
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
}
