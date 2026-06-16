# Normalize compact UI primitives and remove remaining core mocks

## Summary
Create reusable compact UI primitives and ensure core workflow screens use live API contracts rather than production mocks.

## Why
The UI needs consistency, density, and trustworthy data before broader product work continues.

## Scope
- Normalize status badge, risk badge, evidence checklist, step state, and compact toolbar primitives.
- Align components with the Kinetic Industrialism design system.
- Remove production mock responses from workflow, run, run detail, and approval screens.
- Keep test fixtures isolated to tests.

## Acceptance Criteria
- Shared primitives are used consistently across Factory, Runs, Run Detail, and Approvals.
- Production API client does not return mock workflow/run/approval data.
- Loading, empty, and error states exist for core screens.
- Components pass tests and build.

## Verification
- `rg "mock" web/src/api web/src/views web/src/components`
- `npm test`
- `npm run build`

## Suggested Files
- `web/src/components`
- `web/src/api/client.ts`
- `web/src/views`
- `web/src/test`
