# Prioritize enterprise authentication, authorization, and data residency

## Context

German company adoption requires trustworthy self-hosting, SSO/RBAC, audit principals, and clear data boundaries. These are pilot blockers.

## Scope

- Replace the stub auth controller with OIDC/JWT bearer validation.
- Support enterprise identity providers such as Microsoft Entra ID and Keycloak through configuration.
- Add roles such as viewer, operator, approver, and admin.
- Apply authorization policies to workflow publish, run start, approval decisions, connector management, and admin endpoints.
- Ensure audit records use the authenticated principal.
- Document self-hosted data residency boundaries and model-provider configuration choices.

## Acceptance Criteria

- State-changing endpoints require authenticated and authorized users.
- Approval decisions record the real principal.
- Local development still has a documented auth bypass or dev identity mode.
- Deployment docs explain how to configure enterprise SSO and data residency-sensitive settings.
