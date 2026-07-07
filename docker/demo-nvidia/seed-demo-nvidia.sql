-- Agentwerke NVIDIA GLM 5.2 GitHub Issue to PR demo seed.
-- The compose seed service passes the BPMN file as psql variable :bpmn_xml.

INSERT INTO agentwerke.workflows (
    "Id", "Name", "Description", "Version", "Status", "Owner",
    "CreatedAt", "LastEditedAt", "ValidationState", "Tags", "BpmnXml"
) VALUES (
    'wf-demo-nvidia-issue-to-pr',
    'Demo NVIDIA Issue to PR',
    'GitHub issue to requirements, design, sandbox implementation, PR review, merge, and issue close using NVIDIA GLM 5.2 through LiteLLM.',
    'v1.0.0',
    'active',
    'isartor-ai',
    '2026-07-06T00:00:00.000Z',
    '2026-07-06T00:00:00.000Z',
    'valid',
    '["demo","github-trigger","nvidia","litellm","issue-to-pr"]',
    :'bpmn_xml'
) ON CONFLICT ("Id") DO UPDATE SET
    "Name" = EXCLUDED."Name",
    "Description" = EXCLUDED."Description",
    "Version" = EXCLUDED."Version",
    "Status" = EXCLUDED."Status",
    "Owner" = EXCLUDED."Owner",
    "LastEditedAt" = EXCLUDED."LastEditedAt",
    "ValidationState" = EXCLUDED."ValidationState",
    "Tags" = EXCLUDED."Tags",
    "BpmnXml" = EXCLUDED."BpmnXml";
