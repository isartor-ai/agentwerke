# Contributing

## Prerequisites
- .NET SDK 9.0.203 or compatible feature band
- GitHub CLI (optional, for issue and PR workflows)

## Local Setup
1. Restore dependencies:
   - `dotnet restore Agentwerke.sln`
2. Build solution:
   - `dotnet build Agentwerke.sln`
3. Run tests:
   - `dotnet test Agentwerke.sln --no-build`

## Coding Guidelines
- Follow clean architecture boundaries:
  - `Domain` has no infrastructure dependencies.
  - `Application` depends on `Domain` only.
  - `Infrastructure` implements external concerns.
- Keep all I/O async and cancellation-aware.
- Do not bypass policy enforcement for sensitive actions.
- Do not commit secrets or credentials.

## Pull Requests
- Link each PR to one or more GitHub issues.
- Include test evidence (new or existing).
- Document migration impact when persistence changes.
- Highlight security and policy implications for behavior changes.
