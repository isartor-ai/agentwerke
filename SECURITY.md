# Security Policy

Autofac runs autonomous agents against real repositories and infrastructure, so
we take security seriously. Thank you for helping keep Autofac and its users safe.

## Reporting a vulnerability

**Please do not open a public issue for security vulnerabilities.**

Report privately via either:

- **GitHub** — open a private advisory at
  **Security → Advisories → Report a vulnerability** on this repository
  (preferred), or
- **Email** — security@autofac.de

Please include:

- a description of the issue and its impact,
- steps to reproduce (a proof of concept if possible),
- affected version / commit, and
- any suggested remediation.

We aim to acknowledge reports within **3 business days** and to provide a
remediation plan or timeline within **10 business days**. We'll keep you updated
through resolution and credit you in the advisory unless you prefer to remain
anonymous.

## Supported versions

Autofac is pre-1.0 and under active development. Security fixes are applied to
the `main` branch; once 1.0 ships, this section will list supported release lines.

| Version | Supported |
| --- | --- |
| `main` (latest) | ✅ |
| pre-1.0 tags | ⚠️ best effort |

## Scope & hardening notes

Autofac's threat model centers on giving AI agents *bounded* access. Areas of
particular interest for reports:

- Tool Gateway / policy bypass (an agent performing an action outside its
  granted permissions).
- Sandbox escape or egress beyond a profile's network policy.
- Secret/credential exposure (model keys, GitHub tokens) in logs, prompts,
  evidence packs, or committed artifacts.
- Authentication/authorization gaps in the API.

When self-hosting, do not run with the development JWT/dev-token settings in
production (see the deployment docs).
