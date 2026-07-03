import { buildConfiguredTemplateBpmn } from '../templates/templateBpmn';
import type { TemplateDetail, TemplateFactoryConfiguration } from '../types';

const issueToPrTemplate: TemplateDetail = {
  id: 'issue-to-pr',
  name: 'Issue to Pull Request',
  description: 'Create a pull request from an issue.',
  trigger: 'manual',
  policyLevel: 'standard',
  tags: ['sdlc', 'github'],
  agentRoles: ['specification-agent', 'implementation-agent'],
  approvalRoles: ['developer'],
  requiredInputs: ['issue_url', 'repository'],
  evidenceExpectations: ['spec_document', 'code_changes'],
  bpmnXml: `<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL" xmlns:agentwerke="https://agentwerke.de/bpmn/extensions/v1">
  <bpmn:process id="IssueToPr" name="Issue to Pull Request">
    <bpmn:serviceTask id="Specify" name="Specify">
      <bpmn:extensionElements>
        <agentwerke:agentTask agent="specification-agent" action="spec.generate" purposeType="specification" policyTag="sdlc-spec" />
      </bpmn:extensionElements>
    </bpmn:serviceTask>
    <bpmn:serviceTask id="Implement" name="Implement">
      <bpmn:extensionElements>
        <agentwerke:agentTask agent="implementation-agent" action="code.generate" purposeType="implementation" policyTag="repo-change" />
      </bpmn:extensionElements>
    </bpmn:serviceTask>
    <bpmn:userTask id="CodeReview" name="Code Review Approval">
      <bpmn:extensionElements>
        <agentwerke:approvalTask purposeType="code_review" policyTag="human-code-review" />
      </bpmn:extensionElements>
    </bpmn:userTask>
  </bpmn:process>
</bpmn:definitions>`,
};

describe('buildConfiguredTemplateBpmn', () => {
  it('writes structured template settings into the generated BPMN XML', () => {
    const configuration: TemplateFactoryConfiguration = {
      name: 'Payments Issue to PR',
      description: 'Configured workflow for payments repository.',
      owner: 'platform-eng',
      requiredInputs: {
        issue_url: 'https://github.com/acme/payments/issues/42',
        repository: 'acme/payments',
      },
      agentAssignments: {
        'specification-agent': 'ba-agent',
        'implementation-agent': 'code-agent',
      },
      approvalAssignments: {
        developer: 'payments-maintainer',
      },
      connectors: {
        github: true,
        jira: false,
        ci: true,
        slack: false,
      },
      policyLevel: 'elevated',
      evidence: {
        spec_document: true,
        code_changes: true,
      },
    };

    const xml = buildConfiguredTemplateBpmn(issueToPrTemplate, configuration);

    expect(xml).toContain('name="Payments Issue to PR"');
    expect(xml).toContain('agentwerke:owner="platform-eng"');
    expect(xml).toContain('agentwerke:description="Configured workflow for payments repository."');
    expect(xml).toContain('agent="ba-agent"');
    expect(xml).toContain('agent="code-agent"');
    expect(xml).toContain('approvalRole="payments-maintainer"');
    expect(xml).toContain('agentwerke:requiredInputs="issue_url,repository"');
    expect(xml).toContain('&quot;issue_url&quot;:&quot;https://github.com/acme/payments/issues/42&quot;');
    expect(xml).toContain('agentwerke:connectors="github,ci"');
    expect(xml).toContain('agentwerke:policyLevel="elevated"');
    expect(xml).toContain('agentwerke:evidence="spec_document,code_changes"');
    expect(xml).toContain('<bpmndi:BPMNDiagram');
    expect(xml).toContain('bpmnElement="Specify"');
    expect(xml).toContain('bpmnElement="CodeReview"');
  });
});
