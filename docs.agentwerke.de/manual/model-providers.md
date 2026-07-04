# Model Providers

Agentwerke selects its model provider through the `Anthropic` configuration section. The section name remains `Anthropic` for compatibility, but it can select Anthropic, OpenAI-compatible endpoints, LiteLLM, Azure OpenAI through LiteLLM or OpenAI-compatible routing, the mock provider, or automatic behavior.

## Provider choices

| Provider | Use when |
| --- | --- |
| `mock` | You need deterministic, tokenless demos, local development, or CI. |
| `anthropic` | You are using Anthropic directly. |
| `openai` | You are using an OpenAI Chat Completions-compatible endpoint. |
| `litellm` | You route model traffic through LiteLLM or another compatible proxy. |
| `auto` | You want Agentwerke to infer behavior from configured credentials. |

When no usable provider is configured, Agentwerke uses a safe null client and agent steps report that no model is configured.

## Configure a mock provider

Use this for local demos and tests:

```bash
Anthropic__Provider=mock
```

Mock runs should produce deterministic output and zero model cost.

## Configure a real provider

Set the provider, API key, model, and limits through environment variables, secret stores, appsettings, Helm values, or the Admin Settings page.

```bash
Anthropic__Provider=anthropic
Anthropic__ApiKey=<secret>
Anthropic__Model=claude-sonnet-4-6
Anthropic__MaxTokens=4096
Anthropic__MaxToolIterations=10
```

For an OpenAI-compatible or LiteLLM endpoint:

```bash
Anthropic__Provider=litellm
Anthropic__ApiBaseUrl=http://litellm:4000/v1
Anthropic__ApiKey=<secret>
Anthropic__Model=<model-name>
```

## Configure run budgets

Use budgets to prevent runaway agent loops:

```bash
Anthropic__MaxRunCostUsd=2.00
Anthropic__MaxRunTokens=100000
```

Set either value to `0` for unlimited. When a run reaches its budget, Agentwerke stops further model calls with a `budget_exceeded` status.

## Rotate model secrets

Administrators can rotate supported model secrets from Settings. Secret inputs are write-only, the browser clears the value after save, and the API returns only configured/missing status, source, and fingerprint.

For production, prefer deployment-managed secrets over local file-backed settings writes:

```bash
Settings__AllowLocalSecretWrites=false
```

## Readiness checks

Use the Settings readiness check for model providers before running production workflows. The check validates local configuration completeness and writes a redacted audit record. It should not mutate external systems.
