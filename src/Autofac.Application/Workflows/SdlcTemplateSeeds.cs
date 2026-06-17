using Autofac.Domain.Persistence;

namespace Autofac.Application.Workflows;

/// <summary>
/// The built-in SDLC template catalog. All templates use only the Autofac runtime's governed
/// BPMN subset, so they run without Camunda and validate cleanly out of the box.
/// </summary>
public static class SdlcTemplateSeeds
{
    public static readonly SdlcTemplate IssueToPr = new()
    {
        Id = "issue-to-pr",
        Name = "Issue to Pull Request",
        Description = "Full specification, planning, implementation, and code-review cycle that ends by opening a pull request.",
        Trigger = "manual",
        RequiredInputs = ["issue_url", "repository"],
        AgentRoles = ["specification-agent", "planning-agent", "implementation-agent", "github-agent"],
        ApprovalRoles = ["developer"],
        EvidenceExpectations = ["spec_document", "implementation_plan", "code_changes"],
        PolicyLevel = "standard",
        Tags = ["sdlc", "feature", "github"],
        BpmnXml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:autofac="https://autofac.dev/bpmn/extensions/v1">
              <bpmn:process id="IssueToPr" name="Issue to PR">
                <bpmn:startEvent id="Start" name="Issue Received" />
                <bpmn:serviceTask id="Specify" name="Specify Requirements">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="specification-agent"
                      action="spec.generate"
                      purposeType="specification"
                      policyTag="sdlc-spec" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:serviceTask id="Plan" name="Plan Implementation">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="planning-agent"
                      action="plan.generate"
                      purposeType="planning"
                      policyTag="sdlc-plan" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:serviceTask id="Implement" name="Implement Changes">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="implementation-agent"
                      action="code.generate"
                      purposeType="implementation"
                      policyTag="repo-change" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:userTask id="CodeReview" name="Code Review Approval">
                  <bpmn:extensionElements>
                    <autofac:approvalTask
                      purposeType="code_review"
                      policyTag="human-code-review" />
                  </bpmn:extensionElements>
                </bpmn:userTask>
                <bpmn:serviceTask id="OpenPR" name="Open Pull Request">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="github-agent"
                      action="github.open_pr"
                      purposeType="pull_request"
                      policyTag="repo-write" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:endEvent id="End" name="PR Opened" />
                <bpmn:sequenceFlow id="sf1" sourceRef="Start" targetRef="Specify" />
                <bpmn:sequenceFlow id="sf2" sourceRef="Specify" targetRef="Plan" />
                <bpmn:sequenceFlow id="sf3" sourceRef="Plan" targetRef="Implement" />
                <bpmn:sequenceFlow id="sf4" sourceRef="Implement" targetRef="CodeReview" />
                <bpmn:sequenceFlow id="sf5" sourceRef="CodeReview" targetRef="OpenPR" />
                <bpmn:sequenceFlow id="sf6" sourceRef="OpenPR" targetRef="End" />
              </bpmn:process>
            </bpmn:definitions>
            """,
    };

    public static readonly SdlcTemplate Bugfix = new()
    {
        Id = "bugfix",
        Name = "Bugfix",
        Description = "Root-cause diagnosis with configured retries, fix implementation, and test/merge approval gate.",
        Trigger = "manual",
        RequiredInputs = ["bug_report_url", "repository"],
        AgentRoles = ["analysis-agent", "implementation-agent"],
        ApprovalRoles = ["developer"],
        EvidenceExpectations = ["diagnosis_report", "fix_diff"],
        PolicyLevel = "standard",
        Tags = ["sdlc", "bugfix"],
        BpmnXml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:autofac="https://autofac.dev/bpmn/extensions/v1">
              <bpmn:process id="Bugfix" name="Bugfix">
                <bpmn:startEvent id="Start" name="Bug Reported" />
                <bpmn:serviceTask id="Diagnose" name="Diagnose Root Cause">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="analysis-agent"
                      action="bug.diagnose"
                      purposeType="diagnosis"
                      policyTag="read-only"
                      maxRetries="2"
                      retryBackoffSeconds="5" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:serviceTask id="Fix" name="Implement Fix">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="implementation-agent"
                      action="code.fix"
                      purposeType="bugfix"
                      policyTag="repo-change" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:userTask id="TestApproval" name="Test and Merge Approval">
                  <bpmn:extensionElements>
                    <autofac:approvalTask
                      purposeType="bugfix_merge"
                      policyTag="human-merge-approval" />
                  </bpmn:extensionElements>
                </bpmn:userTask>
                <bpmn:endEvent id="End" name="Fix Merged" />
                <bpmn:sequenceFlow id="sf1" sourceRef="Start" targetRef="Diagnose" />
                <bpmn:sequenceFlow id="sf2" sourceRef="Diagnose" targetRef="Fix" />
                <bpmn:sequenceFlow id="sf3" sourceRef="Fix" targetRef="TestApproval" />
                <bpmn:sequenceFlow id="sf4" sourceRef="TestApproval" targetRef="End" />
              </bpmn:process>
            </bpmn:definitions>
            """,
    };

    public static readonly SdlcTemplate Hotfix = new()
    {
        Id = "hotfix",
        Name = "Hotfix (Emergency)",
        Description = "Expedited emergency fix flow with a mandatory human approval gate before deploying directly to production.",
        Trigger = "manual",
        RequiredInputs = ["incident_url", "repository"],
        AgentRoles = ["implementation-agent", "deployment-agent"],
        ApprovalRoles = ["on-call-engineer"],
        EvidenceExpectations = ["fix_diff", "human_approval"],
        PolicyLevel = "critical",
        Tags = ["sdlc", "hotfix", "incident"],
        BpmnXml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:autofac="https://autofac.dev/bpmn/extensions/v1">
              <bpmn:process id="Hotfix" name="Hotfix (Emergency)">
                <bpmn:startEvent id="Start" name="Incident Declared" />
                <bpmn:serviceTask id="EmergencyFix" name="Implement Emergency Fix">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="implementation-agent"
                      action="code.fix"
                      purposeType="hotfix"
                      policyTag="repo-change" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:userTask id="ExpeditedApproval" name="Expedited Approval">
                  <bpmn:extensionElements>
                    <autofac:approvalTask
                      purposeType="emergency_deployment"
                      policyTag="human-emergency-approval" />
                  </bpmn:extensionElements>
                </bpmn:userTask>
                <bpmn:serviceTask id="EmergencyDeploy" name="Deploy Emergency Fix">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="deployment-agent"
                      action="cloud.deploy"
                      purposeType="hotfix_deployment"
                      policyTag="production-write"
                      requiresEvidence="fix_diff,human_approval" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:endEvent id="End" name="Incident Resolved" />
                <bpmn:sequenceFlow id="sf1" sourceRef="Start" targetRef="EmergencyFix" />
                <bpmn:sequenceFlow id="sf2" sourceRef="EmergencyFix" targetRef="ExpeditedApproval" />
                <bpmn:sequenceFlow id="sf3" sourceRef="ExpeditedApproval" targetRef="EmergencyDeploy" />
                <bpmn:sequenceFlow id="sf4" sourceRef="EmergencyDeploy" targetRef="End" />
              </bpmn:process>
            </bpmn:definitions>
            """,
    };

    public static readonly SdlcTemplate DeploymentApproval = new()
    {
        Id = "deployment-approval",
        Name = "Parallel Build, Test and Deploy",
        Description = "Parallel quality gate (tests + security scan) followed by a deploy-approval gate and production deployment.",
        Trigger = "manual",
        RequiredInputs = ["build_artifact", "target_environment"],
        AgentRoles = ["testing-agent", "security-agent", "deployment-agent"],
        ApprovalRoles = ["release-manager"],
        EvidenceExpectations = ["tests_passed", "security_cleared", "human_approval"],
        PolicyLevel = "elevated",
        Tags = ["sdlc", "deployment", "quality-gate"],
        BpmnXml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:autofac="https://autofac.dev/bpmn/extensions/v1">
              <bpmn:process id="ParallelBuildAndTest" name="Parallel Build and Test">
                <bpmn:startEvent id="Start" name="Build Triggered" />
                <bpmn:parallelGateway id="Fork" name="Quality Gate Fork" />
                <bpmn:serviceTask id="RunTests" name="Run Test Suite">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="testing-agent"
                      action="tests.run"
                      purposeType="quality_assurance"
                      policyTag="read-only" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:serviceTask id="SecurityScan" name="Run Security Scan">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="security-agent"
                      action="security.scan"
                      purposeType="security_review"
                      policyTag="read-only" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:parallelGateway id="Join" name="Quality Gate Join" />
                <bpmn:userTask id="DeployApproval" name="Deploy Approval">
                  <bpmn:extensionElements>
                    <autofac:approvalTask
                      purposeType="production_deployment"
                      policyTag="human-deploy-approval" />
                  </bpmn:extensionElements>
                </bpmn:userTask>
                <bpmn:serviceTask id="Deploy" name="Deploy to Production">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="deployment-agent"
                      action="cloud.deploy"
                      purposeType="production_deployment"
                      policyTag="production-write"
                      requiresEvidence="tests_passed,security_cleared,human_approval" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:endEvent id="End" name="Deployed" />
                <bpmn:sequenceFlow id="sf1" sourceRef="Start" targetRef="Fork" />
                <bpmn:sequenceFlow id="sf2" sourceRef="Fork" targetRef="RunTests" />
                <bpmn:sequenceFlow id="sf3" sourceRef="Fork" targetRef="SecurityScan" />
                <bpmn:sequenceFlow id="sf4" sourceRef="RunTests" targetRef="Join" />
                <bpmn:sequenceFlow id="sf5" sourceRef="SecurityScan" targetRef="Join" />
                <bpmn:sequenceFlow id="sf6" sourceRef="Join" targetRef="DeployApproval" />
                <bpmn:sequenceFlow id="sf7" sourceRef="DeployApproval" targetRef="Deploy" />
                <bpmn:sequenceFlow id="sf8" sourceRef="Deploy" targetRef="End" />
              </bpmn:process>
            </bpmn:definitions>
            """,
    };

    public static readonly SdlcTemplate SecurityReview = new()
    {
        Id = "security-review",
        Name = "Security Review",
        Description = "Automated security scan with a timer-gated report wait, conditional remediation branch, and sign-off approval.",
        Trigger = "manual",
        RequiredInputs = ["repository", "scan_scope"],
        AgentRoles = ["security-agent"],
        ApprovalRoles = ["security-engineer"],
        EvidenceExpectations = ["scan_report", "remediation_evidence"],
        PolicyLevel = "elevated",
        Tags = ["sdlc", "security", "compliance"],
        BpmnXml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:autofac="https://autofac.dev/bpmn/extensions/v1">
              <bpmn:process id="SecurityReview" name="Security Review">
                <bpmn:startEvent id="Start" name="Review Requested" />
                <bpmn:serviceTask id="Scan" name="Run Security Scan">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="security-agent"
                      action="security.scan"
                      purposeType="security_review"
                      policyTag="read-only" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:intermediateCatchEvent id="WaitForReport" name="Wait for Scan Report">
                  <bpmn:timerEventDefinition>
                    <bpmn:timeDuration>PT30S</bpmn:timeDuration>
                  </bpmn:timerEventDefinition>
                </bpmn:intermediateCatchEvent>
                <bpmn:exclusiveGateway id="SeverityGate" name="Findings Severity Gate" />
                <bpmn:serviceTask id="Remediate" name="Remediate Findings">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="security-agent"
                      action="security.remediate"
                      purposeType="remediation"
                      policyTag="repo-change" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:userTask id="VerifyApproval" name="Security Sign-Off">
                  <bpmn:extensionElements>
                    <autofac:approvalTask
                      purposeType="security_sign_off"
                      policyTag="human-security-approval" />
                  </bpmn:extensionElements>
                </bpmn:userTask>
                <bpmn:endEvent id="End" name="Security Cleared" />
                <bpmn:sequenceFlow id="sf1" sourceRef="Start" targetRef="Scan" />
                <bpmn:sequenceFlow id="sf2" sourceRef="Scan" targetRef="WaitForReport" />
                <bpmn:sequenceFlow id="sf3" sourceRef="WaitForReport" targetRef="SeverityGate" />
                <bpmn:sequenceFlow id="sf4" sourceRef="SeverityGate" targetRef="Remediate">
                  <bpmn:conditionExpression>true</bpmn:conditionExpression>
                </bpmn:sequenceFlow>
                <bpmn:sequenceFlow id="sf5" sourceRef="SeverityGate" targetRef="VerifyApproval" />
                <bpmn:sequenceFlow id="sf6" sourceRef="Remediate" targetRef="VerifyApproval" />
                <bpmn:sequenceFlow id="sf7" sourceRef="VerifyApproval" targetRef="End" />
              </bpmn:process>
            </bpmn:definitions>
            """,
    };

    public static readonly SdlcTemplate ReleaseApproval = new()
    {
        Id = "release-approval",
        Name = "Release Approval",
        Description = "Packages a release, generates release notes, and gates deployment behind dual QA and executive approval.",
        Trigger = "manual",
        RequiredInputs = ["version_tag", "repository", "changelog"],
        AgentRoles = ["build-agent", "documentation-agent", "deployment-agent"],
        ApprovalRoles = ["qa-lead", "executive-sponsor"],
        EvidenceExpectations = ["tests_passed", "security_cleared", "release_notes_generated"],
        PolicyLevel = "critical",
        Tags = ["sdlc", "release", "compliance"],
        BpmnXml = """
            <bpmn:definitions
                xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
                xmlns:autofac="https://autofac.dev/bpmn/extensions/v1">
              <bpmn:process id="ReleaseApproval" name="Release Approval">
                <bpmn:startEvent id="Start" name="Release Initiated" />
                <bpmn:serviceTask id="Package" name="Package Release Artifact">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="build-agent"
                      action="build.package"
                      purposeType="release_packaging"
                      policyTag="build-write" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:serviceTask id="Notes" name="Generate Release Notes">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="documentation-agent"
                      action="docs.release_notes"
                      purposeType="documentation"
                      policyTag="read-only" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:userTask id="QAApproval" name="QA Sign-Off">
                  <bpmn:extensionElements>
                    <autofac:approvalTask
                      purposeType="qa_sign_off"
                      policyTag="human-qa-approval" />
                  </bpmn:extensionElements>
                </bpmn:userTask>
                <bpmn:userTask id="ExecApproval" name="Executive Approval">
                  <bpmn:extensionElements>
                    <autofac:approvalTask
                      purposeType="executive_sign_off"
                      policyTag="human-exec-approval" />
                  </bpmn:extensionElements>
                </bpmn:userTask>
                <bpmn:serviceTask id="DeployRelease" name="Deploy Release">
                  <bpmn:extensionElements>
                    <autofac:agentTask
                      agent="deployment-agent"
                      action="cloud.deploy"
                      purposeType="release_deployment"
                      policyTag="production-write"
                      requiresEvidence="tests_passed,security_cleared,release_notes_generated" />
                  </bpmn:extensionElements>
                </bpmn:serviceTask>
                <bpmn:endEvent id="End" name="Release Deployed" />
                <bpmn:sequenceFlow id="sf1" sourceRef="Start" targetRef="Package" />
                <bpmn:sequenceFlow id="sf2" sourceRef="Package" targetRef="Notes" />
                <bpmn:sequenceFlow id="sf3" sourceRef="Notes" targetRef="QAApproval" />
                <bpmn:sequenceFlow id="sf4" sourceRef="QAApproval" targetRef="ExecApproval" />
                <bpmn:sequenceFlow id="sf5" sourceRef="ExecApproval" targetRef="DeployRelease" />
                <bpmn:sequenceFlow id="sf6" sourceRef="DeployRelease" targetRef="End" />
              </bpmn:process>
            </bpmn:definitions>
            """,
    };

    public static IReadOnlyList<SdlcTemplate> All { get; } =
    [
        IssueToPr,
        Bugfix,
        Hotfix,
        DeploymentApproval,
        SecurityReview,
        ReleaseApproval,
    ];
}
