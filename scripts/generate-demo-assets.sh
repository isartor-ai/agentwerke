#!/usr/bin/env bash
set -euo pipefail

ROOT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")/.." && pwd)"
cd "$ROOT_DIR"

# Ensure node/npm and homebrew/miniconda binaries (ffmpeg, docker) are on PATH
if [ -d "$HOME/.nvm/versions/node" ]; then
  LATEST_NODE="$(ls "$HOME/.nvm/versions/node" | tail -n 1)"
  export PATH="$HOME/.nvm/versions/node/$LATEST_NODE/bin:$PATH"
fi
export PATH="/opt/homebrew/bin:/usr/local/bin:$HOME/miniconda3/bin:$PATH"

COMPOSE_FILE="docker/docker-compose.manual.yml"
API_URL="${DEMO_API_URL:-http://localhost:8081}"
WEB_URL="${DEMO_BASE_URL:-http://localhost:3002}"

echo "==> [1/6] Ensuring Agentwerke manual showcase stack is running..."
if curl -fsS "$API_URL/api/auth/me" >/dev/null 2>&1 && curl -fsS "$WEB_URL" >/dev/null 2>&1; then
  echo "    Manual stack already running."
else
  docker compose -f "$COMPOSE_FILE" up -d
fi

echo "==> [2/6] Waiting for API ($API_URL/api/auth/me) and Web UI ($WEB_URL)..."
for i in $(seq 1 60); do
  if curl -fsS "$API_URL/api/auth/me" >/dev/null 2>&1 && curl -fsS "$WEB_URL" >/dev/null 2>&1; then
    echo "    Stack is healthy!"
    break
  fi
  if [ "$i" -eq 60 ]; then
    echo "ERROR: Timed out waiting for stack readiness." >&2
    exit 1
  fi
  sleep 2
done

echo "==> [3/6] Seeding deterministic V-Model showcase dataset..."
docker compose -f "$COMPOSE_FILE" exec -T postgres \
  psql -v ON_ERROR_STOP=1 -U postgres -d agentwerke_manual < docker/seed-showcase.sql

echo "==> [4/6] Uploading V-Model showcase artifacts..."
upload_artifact() {
  local run_id="$1"
  local name="$2"
  local content_type="$3"
  local body="$4"

  curl -fsS -X POST "$API_URL/api/runs/$run_id/artifacts/$name" \
    -H "Content-Type: $content_type" \
    --data-binary "$body" >/dev/null
}

read -r -d '' CHECKLIST_MD <<'EOF' || true
# Production Release Readiness Checklist — V-Model Gate

| Attribute | Value |
| :--- | :--- |
| **Workflow** | `V-Model Autonomous SDLC Pipeline (v2.4.0)` |
| **Traceability Coverage** | **100% (7 / 7 V-Model Obligations Verified)** |
| **Sandbox Profile** | `ghcr.io/isartor-ai/dotnet-sandbox:8.0` (`--network=none`) |
| **Security Guardrails** | `secret-and-key-scanner` (PASS), `sandbox-egress-firewall` (PASS) |

## Bilateral V-Model Verification Matrix

- [x] **REQ-204-1 (PKCE S256 Enforcement)**: Verified by `ExchangeAsync_Enforces_PKCE_S256_CodeVerifier` (`920ms`)
- [x] **REQ-204-2 (Atomic Refresh Rotation)**: Verified by `ExchangeAsync_Rotates_RefreshToken_Lineage_Atomically` (`1140ms`)
- [x] **REQ-204-3 (Replay Family Revocation)**: Verified by `ExchangeAsync_Revokes_Entire_TokenFamily_On_Replay` (`1060ms`)
- [x] **REQ-204-4 (Structured Anomaly Audit)**: Verified by `ExchangeAsync_Emits_AuditRecord_On_Replay_Anomaly` (`720ms`)

## Operational & Rollback Sign-Off

1. **Zero-Downtime Migration**: Online index creation verified against staging settlement replica.
2. **Rollback Plan**: Feature flag `Auth:EnforceStrictTokenFamilyLineage` can be toggled cleanly without schema rollback.
EOF

read -r -d '' REQ_SPEC_MD <<'EOF' || true
# Requirements Specification — CHG-204: OAuth 2.1 PKCE & Refresh Token Rotation

## Source Issue
- **GitHub Issue**: `isartor-ai/agentwerke#204`
- **Classification**: Tier-1 Security Control (`OAuth 2.1` / `RFC 7636`)

## Traceable Obligations
1. **REQ-204-1**: Reject authorization code exchanges lacking a valid `S256` PKCE `code_verifier`.
2. **REQ-204-2**: Issue a new refresh token within the same `TokenFamilyId` on every token refresh.
3. **REQ-204-3**: Immediately revoke the entire `TokenFamilyId` lineage if a consumed refresh token is replayed.
4. **REQ-204-4**: Record an immutable `security.token_family.revoked` audit trail entry on replay detection.
EOF

read -r -d '' ADR_MD <<'EOF' || true
# ADR-004: Family-Chained Refresh Token Lineage & Constant-Time PKCE Verification

- **Status**: Accepted (`V-Model System Architecture Stage`)
- **Traces To**: `REQ-204-1`, `REQ-204-2`, `REQ-204-3`, `REQ-204-4`

## Decision
1. Store `TokenFamilyId` and `ParentTokenHash` on `RefreshTokenRecord` in PostgreSQL.
2. Enforce `CryptographicOperations.FixedTimeEquals` when comparing the SHA-256 digest of `code_verifier` against `code_challenge`.
EOF

read -r -d '' DIFF_PATCH <<'EOF' || true
diff --git a/src/Auth/TokenRefreshService.cs b/src/Auth/TokenRefreshService.cs
index 4d91a0e..8f4e2a9 100644
--- a/src/Auth/TokenRefreshService.cs
+++ b/src/Auth/TokenRefreshService.cs
@@ -42,8 +42,24 @@ public sealed class TokenRefreshService : ITokenRefreshService
     public async Task<TokenExchangeResult> ExchangeAsync(TokenRequest request, CancellationToken ct)
     {
+        if (!PkceChallengeValidator.VerifyS256(request.CodeVerifier, request.CodeChallenge))
+        {
+            throw new SecurityTokenValidationException("PKCE S256 verification failed.");
+        }
+
         var storedToken = await _tokenStore.FindByHashAsync(request.RefreshTokenHash, ct);
+        if (storedToken.IsConsumed)
+        {
+            await _tokenStore.RevokeFamilyAsync(storedToken.TokenFamilyId, "replay_detected", ct);
+            await _auditLog.RecordReplayAnomalyAsync(storedToken.TokenFamilyId, request.SubjectId, ct);
+            throw new SecurityTokenReplayException("Refresh token replay detected; token family revoked.");
+        }
+
+        return await _tokenStore.RotateWithinFamilyAsync(storedToken, ct);
     }
 }
EOF

read -r -d '' JUNIT_XML <<'EOF' || true
<?xml version="1.0" encoding="utf-8"?>
<testsuites name="Agentwerke.Auth.Tests" tests="4" failures="0" errors="0" skipped="0" time="3.840">
  <testsuite name="Agentwerke.Auth.Tests.TokenRefreshServiceTests" tests="4" failures="0" errors="0" skipped="0" time="3.840">
    <testcase classname="Agentwerke.Auth.Tests.TokenRefreshServiceTests" name="ExchangeAsync_Enforces_PKCE_S256_CodeVerifier" time="0.920" />
    <testcase classname="Agentwerke.Auth.Tests.TokenRefreshServiceTests" name="ExchangeAsync_Rotates_RefreshToken_Lineage_Atomically" time="1.140" />
    <testcase classname="Agentwerke.Auth.Tests.TokenRefreshServiceTests" name="ExchangeAsync_Revokes_Entire_TokenFamily_On_Replay" time="1.060" />
    <testcase classname="Agentwerke.Auth.Tests.TokenRefreshServiceTests" name="ExchangeAsync_Emits_AuditRecord_On_Replay_Anomaly" time="0.720" />
  </testsuite>
</testsuites>
EOF

for run in "run-showcase-vmodel-001" "run-showcase-approval-002"; do
  upload_artifact "$run" "release-readiness-checklist.md" "text/markdown" "$CHECKLIST_MD"
  upload_artifact "$run" "requirements-spec.md" "text/markdown" "$REQ_SPEC_MD"
  upload_artifact "$run" "architecture-adr-004.md" "text/markdown" "$ADR_MD"
  upload_artifact "$run" "implementation-diff.patch" "text/x-diff" "$DIFF_PATCH"
  upload_artifact "$run" "junit-results.xml" "application/xml" "$JUNIT_XML"
done

echo "==> [5/6] Capturing Retina screenshots & recording video tour via Playwright..."
mkdir -p docs/assets/screenshots docs/assets/demo
rm -f docs/assets/screenshots/*.png
(
  cd web
  DEMO_BASE_URL="$WEB_URL" npx playwright test --config=playwright.demo.config.ts --project=screenshots
  DEMO_BASE_URL="$WEB_URL" npx playwright test --config=playwright.demo.config.ts --project=video
)

echo "==> [6/6] Post-processing video assets with ffmpeg..."
RAW_WEBM="$(find web/test-results/demo-artifacts -name '*.webm' | head -n 1)"
if [ -z "$RAW_WEBM" ]; then
  echo "ERROR: No recorded .webm found in web/test-results/demo-artifacts" >&2
  exit 1
fi

cp "$RAW_WEBM" docs/assets/demo/agentwerke-demo.webm

ffmpeg -y -i "$RAW_WEBM" \
  -c:v libx264 -preset slow -crf 18 -pix_fmt yuv420p \
  -r 30 -movflags +faststart \
  docs/assets/demo/agentwerke-demo.mp4

ffmpeg -y -i docs/assets/demo/agentwerke-demo.mp4 \
  -vf "fps=10,scale=960:-1:flags=lanczos,split[s0][s1];[s0]palettegen=max_colors=128[p];[s1][p]paletteuse=dither=bayer" \
  docs/assets/demo/agentwerke-demo-preview.gif

echo "==> Demo asset generation complete!"
ls -lh docs/assets/screenshots/*.png docs/assets/demo/*
