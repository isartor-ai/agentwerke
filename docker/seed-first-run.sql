-- Agentwerke first-run seed data
-- Idempotent: the sample workflow is inserted only when it does not already exist.
-- Run after EF Core migrations have created the schema.

INSERT INTO agentwerke.workflows (
    "Id", "Name", "Description", "Version", "Status", "Owner",
    "CreatedAt", "LastEditedAt", "ValidationState", "Tags", "BpmnXml"
) VALUES (
    'wf-first-run-sample',
    'First Run Sample',
    'Seeded workflow for fresh Agentwerke installs: a sample agent task followed by a review approval.',
    'v1.0.0',
    'active',
    'agentwerke',
    '2026-06-29T00:00:00.000Z',
    '2026-06-29T00:00:00.000Z',
    'valid',
    '["sample","quickstart","first-run"]',
    $bpmn$<?xml version="1.0" encoding="UTF-8"?>
<bpmn:definitions
    xmlns:bpmn="http://www.omg.org/spec/BPMN/20100524/MODEL"
    xmlns:bpmndi="http://www.omg.org/spec/BPMN/20100524/DI"
    xmlns:dc="http://www.omg.org/spec/DD/20100524/DC"
    xmlns:di="http://www.omg.org/spec/DD/20100524/DI"
    xmlns:agentwerke="https://agentwerke.de/bpmn/extensions/v1"
    id="first-run-sample-defs"
    targetNamespace="https://agentwerke.de/bpmn/extensions/v1">
  <bpmn:process id="FirstRunSample" name="First Run Sample" isExecutable="true">
    <bpmn:startEvent id="Start" name="Start">
      <bpmn:outgoing>Flow1</bpmn:outgoing>
    </bpmn:startEvent>
    <bpmn:serviceTask id="DraftImplementationNote" name="Draft Implementation Note">
      <bpmn:extensionElements>
        <agentwerke:agentTask
          agent="first-run-engineer"
          action="first-run.implement"
          environment="quickstart"
          purposeType="first-run-onboarding"
          policyTag="standard"
          requiresEvidence="agent-output"
          executionMode="local"
          permissionLevel="read-only">
          <agentwerke:prompt>
            Write a concise implementation note for a first Agentwerke run. Mention that the BPMN runtime, policy decision, sample agent, and evidence capture path are working.
          </agentwerke:prompt>
        </agentwerke:agentTask>
      </bpmn:extensionElements>
      <bpmn:incoming>Flow1</bpmn:incoming>
      <bpmn:outgoing>Flow2</bpmn:outgoing>
    </bpmn:serviceTask>
    <bpmn:userTask id="ReviewSampleOutput" name="Review Sample Output">
      <bpmn:extensionElements>
        <agentwerke:approvalTask purposeType="sample-review" policyTag="standard" />
      </bpmn:extensionElements>
      <bpmn:incoming>Flow2</bpmn:incoming>
      <bpmn:outgoing>Flow3</bpmn:outgoing>
    </bpmn:userTask>
    <bpmn:endEvent id="End" name="Done">
      <bpmn:incoming>Flow3</bpmn:incoming>
    </bpmn:endEvent>
    <bpmn:sequenceFlow id="Flow1" sourceRef="Start" targetRef="DraftImplementationNote" />
    <bpmn:sequenceFlow id="Flow2" sourceRef="DraftImplementationNote" targetRef="ReviewSampleOutput" />
    <bpmn:sequenceFlow id="Flow3" sourceRef="ReviewSampleOutput" targetRef="End" />
  </bpmn:process>
  <bpmndi:BPMNDiagram id="BPMNDiagram_1">
    <bpmndi:BPMNPlane id="BPMNPlane_1" bpmnElement="FirstRunSample">
      <bpmndi:BPMNShape id="Start_di" bpmnElement="Start">
        <dc:Bounds x="152" y="142" width="36" height="36" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="DraftImplementationNote_di" bpmnElement="DraftImplementationNote">
        <dc:Bounds x="248" y="120" width="144" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="ReviewSampleOutput_di" bpmnElement="ReviewSampleOutput">
        <dc:Bounds x="456" y="120" width="144" height="80" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNShape id="End_di" bpmnElement="End">
        <dc:Bounds x="664" y="142" width="36" height="36" />
      </bpmndi:BPMNShape>
      <bpmndi:BPMNEdge id="Flow1_di" bpmnElement="Flow1">
        <di:waypoint x="188" y="160" /><di:waypoint x="248" y="160" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow2_di" bpmnElement="Flow2">
        <di:waypoint x="392" y="160" /><di:waypoint x="456" y="160" />
      </bpmndi:BPMNEdge>
      <bpmndi:BPMNEdge id="Flow3_di" bpmnElement="Flow3">
        <di:waypoint x="600" y="160" /><di:waypoint x="664" y="160" />
      </bpmndi:BPMNEdge>
    </bpmndi:BPMNPlane>
  </bpmndi:BPMNDiagram>
</bpmn:definitions>$bpmn$
) ON CONFLICT ("Id") DO NOTHING;
