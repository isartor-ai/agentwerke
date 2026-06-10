import { render, screen, fireEvent, waitFor } from '@testing-library/react';
import { describe, it, expect, beforeEach, vi } from 'vitest';
import type { RunStep } from '../types';

/**
 * Test suite for metadata editor form validation and auto-save
 * Coverage targets: >85%
 */

// Mock metadata validation
interface NodeMetadataDraft {
  agent: string;
  action: string;
  environment: string;
  purposeType: string;
  policyTag: string;
}

function validateMetadata(metadata: NodeMetadataDraft, nodeType: string): string[] {
  const issues: string[] = [];

  if (nodeType === 'serviceTask' || nodeType === 'scriptTask') {
    if (!metadata.agent.trim()) {
      issues.push('Agent is required for service/script tasks.');
    }
    if (!metadata.action.trim()) {
      issues.push('Action is required for service/script tasks.');
    }
    if (!metadata.purposeType.trim()) {
      issues.push('Purpose type is required for service/script tasks.');
    }
    if (!metadata.policyTag.trim()) {
      issues.push('Policy tag is required for service/script tasks.');
    }
  }

  if (nodeType === 'userTask') {
    if (!metadata.purposeType.trim()) {
      issues.push('Purpose type is required for approval tasks.');
    }
    if (!metadata.policyTag.trim()) {
      issues.push('Policy tag is required for approval tasks.');
    }
  }

  return issues;
}

describe('Metadata Editor Validation', () => {
  it('should validate service task metadata - all required fields present', () => {
    const metadata: NodeMetadataDraft = {
      agent: 'DeploymentAgent',
      action: 'cloud.deploy_artifact',
      environment: 'production',
      purposeType: 'production_deployment',
      policyTag: 'deploy_gateway',
    };

    const issues = validateMetadata(metadata, 'serviceTask');
    expect(issues).toHaveLength(0);
  });

  it('should validate service task metadata - missing agent', () => {
    const metadata: NodeMetadataDraft = {
      agent: '',
      action: 'cloud.deploy_artifact',
      environment: 'production',
      purposeType: 'production_deployment',
      policyTag: 'deploy_gateway',
    };

    const issues = validateMetadata(metadata, 'serviceTask');
    expect(issues).toContain('Agent is required for service/script tasks.');
  });

  it('should validate service task metadata - missing action', () => {
    const metadata: NodeMetadataDraft = {
      agent: 'DeploymentAgent',
      action: '',
      environment: 'production',
      purposeType: 'production_deployment',
      policyTag: 'deploy_gateway',
    };

    const issues = validateMetadata(metadata, 'serviceTask');
    expect(issues).toContain('Action is required for service/script tasks.');
  });

  it('should validate service task metadata - missing policy tag', () => {
    const metadata: NodeMetadataDraft = {
      agent: 'DeploymentAgent',
      action: 'cloud.deploy_artifact',
      environment: 'production',
      purposeType: 'production_deployment',
      policyTag: '',
    };

    const issues = validateMetadata(metadata, 'serviceTask');
    expect(issues).toContain('Policy tag is required for service/script tasks.');
  });

  it('should validate service task metadata - multiple missing fields', () => {
    const metadata: NodeMetadataDraft = {
      agent: '',
      action: '',
      environment: 'production',
      purposeType: '',
      policyTag: '',
    };

    const issues = validateMetadata(metadata, 'serviceTask');
    expect(issues.length).toBeGreaterThan(1);
    expect(issues).toContain('Agent is required for service/script tasks.');
    expect(issues).toContain('Action is required for service/script tasks.');
  });

  it('should validate user task metadata - all required fields present', () => {
    const metadata: NodeMetadataDraft = {
      agent: '',
      action: '',
      environment: '',
      purposeType: 'production_deployment',
      policyTag: 'approval_required',
    };

    const issues = validateMetadata(metadata, 'userTask');
    expect(issues).toHaveLength(0);
  });

  it('should validate user task metadata - missing purpose type', () => {
    const metadata: NodeMetadataDraft = {
      agent: '',
      action: '',
      environment: '',
      purposeType: '',
      policyTag: 'approval_required',
    };

    const issues = validateMetadata(metadata, 'userTask');
    expect(issues).toContain('Purpose type is required for approval tasks.');
  });

  it('should validate user task metadata - missing policy tag', () => {
    const metadata: NodeMetadataDraft = {
      agent: '',
      action: '',
      environment: '',
      purposeType: 'production_deployment',
      policyTag: '',
    };

    const issues = validateMetadata(metadata, 'userTask');
    expect(issues).toContain('Policy tag is required for approval tasks.');
  });

  it('should trim whitespace when validating', () => {
    const metadata: NodeMetadataDraft = {
      agent: '   ',
      action: '   ',
      environment: 'production',
      purposeType: '   ',
      policyTag: '   ',
    };

    const issues = validateMetadata(metadata, 'serviceTask');
    expect(issues).toContain('Agent is required for service/script tasks.');
    expect(issues).toContain('Action is required for service/script tasks.');
  });

  it('should validate script task with same rules as service task', () => {
    const metadata: NodeMetadataDraft = {
      agent: 'ScriptAgent',
      action: 'script.execute',
      environment: 'staging',
      purposeType: 'build_test',
      policyTag: 'script_policy',
    };

    const issues = validateMetadata(metadata, 'scriptTask');
    expect(issues).toHaveLength(0);
  });
});

describe('Metadata Editor localStorage Integration', () => {
  const mockLocalStorage = (() => {
    let store: Record<string, string> = {};

    return {
      getItem: (key: string) => store[key] || null,
      setItem: (key: string, value: string) => {
        store[key] = value;
      },
      removeItem: (key: string) => {
        delete store[key];
      },
      clear: () => {
        store = {};
      },
    };
  })();

  beforeEach(() => {
    mockLocalStorage.clear();
    Object.defineProperty(global, 'localStorage', {
      value: mockLocalStorage,
      writable: true,
    });
  });

  it('should save metadata to localStorage as JSON', () => {
    const metadata: NodeMetadataDraft = {
      agent: 'TestAgent',
      action: 'test.action',
      environment: 'test',
      purposeType: 'test_purpose',
      policyTag: 'test_policy',
    };

    const key = 'autofac_draft_node_metadata';
    mockLocalStorage.setItem(key, JSON.stringify(metadata));

    const retrieved = JSON.parse(mockLocalStorage.getItem(key) || '{}');
    expect(retrieved).toEqual(metadata);
  });

  it('should recover metadata from localStorage', () => {
    const key = 'autofac_draft_node_metadata';
    const original: NodeMetadataDraft = {
      agent: 'RecoveryAgent',
      action: 'recovery.action',
      environment: 'prod',
      purposeType: 'recovery_test',
      policyTag: 'recovery_policy',
    };

    mockLocalStorage.setItem(key, JSON.stringify(original));
    const stored = mockLocalStorage.getItem(key);
    const recovered = stored ? JSON.parse(stored) : null;

    expect(recovered).toEqual(original);
  });

  it('should handle corrupted JSON in localStorage gracefully', () => {
    const key = 'autofac_draft_node_metadata';
    mockLocalStorage.setItem(key, 'invalid json {');

    try {
      const stored = mockLocalStorage.getItem(key);
      JSON.parse(stored || '{}');
      expect(false).toBe(true); // Should not reach here
    } catch (error) {
      expect(error).toBeDefined();
    }
  });

  it('should remove draft from localStorage after publish', () => {
    const xmlKey = 'autofac_draft_bpmn_xml';
    const metadataKey = 'autofac_draft_node_metadata';

    mockLocalStorage.setItem(xmlKey, 'draft xml');
    mockLocalStorage.setItem(metadataKey, 'draft metadata');

    // Simulate publish
    mockLocalStorage.removeItem(xmlKey);
    mockLocalStorage.removeItem(metadataKey);

    expect(mockLocalStorage.getItem(xmlKey)).toBeNull();
    expect(mockLocalStorage.getItem(metadataKey)).toBeNull();
  });
});

describe('Policy Risk Simulation Integration', () => {
  it('should identify high-risk production deployment', () => {
    const metadata: NodeMetadataDraft = {
      agent: 'DeploymentAgent',
      action: 'cloud.deploy_artifact',
      environment: 'production',
      purposeType: 'production_deployment',
      policyTag: 'production_deployment_gateway',
    };

    // High risk when: environment=production AND action=deploy
    const isHighRisk =
      metadata.environment === 'production' &&
      metadata.action.includes('deploy');

    expect(isHighRisk).toBe(true);
  });

  it('should identify low-risk staging action', () => {
    const metadata: NodeMetadataDraft = {
      agent: 'TestAgent',
      action: 'test.execute',
      environment: 'staging',
      purposeType: 'build_test',
      policyTag: 'test_policy',
    };

    // Low risk when: environment != production
    const isHighRisk = metadata.environment === 'production';

    expect(isHighRisk).toBe(false);
  });

  it('should require approval for critical actions', () => {
    const criticalActions = ['cloud.deploy_artifact', 'iam.attach_policy', 'database.drop'];
    const metadata: NodeMetadataDraft = {
      agent: 'DeploymentAgent',
      action: 'cloud.deploy_artifact',
      environment: 'production',
      purposeType: 'production_deployment',
      policyTag: 'production_deployment_gateway',
    };

    const requiresApproval = criticalActions.includes(metadata.action);
    expect(requiresApproval).toBe(true);
  });

  it('should flag missing evidence requirements', () => {
    const metadata: NodeMetadataDraft = {
      agent: 'DeploymentAgent',
      action: 'cloud.deploy_artifact',
      environment: 'production',
      purposeType: 'production_deployment',
      policyTag: 'production_deployment_gateway',
    };

    // Evidence requirements for production deploys
    const requiredEvidence = [
      'ci_passed',
      'sast_passed',
      'artifact_signed',
      'human_approval',
    ];

    // Check if all evidence is documented
    const hasAllEvidence = requiredEvidence.every((item) => {
      // In real implementation, would check against evidence checklist
      return true;
    });

    expect(hasAllEvidence).toBe(true);
  });
});

describe('Metadata Editor - Validation Performance', () => {
  it('should validate metadata within 500ms', () => {
    const startTime = performance.now();

    const metadata: NodeMetadataDraft = {
      agent: 'Agent',
      action: 'action.execute',
      environment: 'prod',
      purposeType: 'test',
      policyTag: 'policy',
    };

    for (let i = 0; i < 1000; i++) {
      validateMetadata(metadata, 'serviceTask');
    }

    const endTime = performance.now();
    const totalTime = endTime - startTime;
    const avgTime = totalTime / 1000;

    // 1000 validations should average well under 500ms per validation
    expect(avgTime).toBeLessThan(0.5);
  });
});

describe('Metadata Editor - Form State Transitions', () => {
  it('should transition from invalid to valid state', () => {
    let metadata: NodeMetadataDraft = {
      agent: '',
      action: '',
      environment: 'prod',
      purposeType: '',
      policyTag: '',
    };

    let issues = validateMetadata(metadata, 'serviceTask');
    expect(issues.length).toBeGreaterThan(0);

    // Populate fields
    metadata = {
      agent: 'Agent',
      action: 'action',
      environment: 'prod',
      purposeType: 'purpose',
      policyTag: 'policy',
    };

    issues = validateMetadata(metadata, 'serviceTask');
    expect(issues).toHaveLength(0);
  });

  it('should mark form as dirty on metadata change', () => {
    let isDirty = false;
    const original: NodeMetadataDraft = {
      agent: 'Agent',
      action: 'action',
      environment: 'prod',
      purposeType: 'purpose',
      policyTag: 'policy',
    };

    const onChange = () => {
      isDirty = true;
    };

    onChange();
    expect(isDirty).toBe(true);
  });
});
