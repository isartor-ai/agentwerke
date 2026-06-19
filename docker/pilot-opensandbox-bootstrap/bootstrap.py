import json
import sys
import time
import urllib.error
import urllib.request
from pathlib import Path


API_BASE = "http://api:8080"
TOKEN = (
    "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9."
    "eyJzdWIiOiJkZXY6YWRtaW4iLCJodHRwOi8vc2NoZW1hcy54bWxzb2FwLm9yZy93cy8yMDA1LzA1L2lk"
    "ZW50aXR5L2NsYWltcy9uYW1lIjoiZGV2OmFkbWluIiwiaHR0cDovL3NjaGVtYXMubWljcm9zb2Z0LmNv"
    "bS93cy8yMDA4LzA2L2lkZW50aXR5L2NsYWltcy9yb2xlIjoiQWRtaW4iLCJpc3MiOiJhdXRvZmFjLWRl"
    "diIsImF1ZCI6ImF1dG9mYWMtZGV2IiwiZXhwIjoxODkzNDU2MDAwfQ."
    "1koOCkdx_pfBXg8WIobkTotJevt-3H2ofM66IecvVmQ"
)
AGENT_FILE = Path("/bootstrap/opensandbox-pilot-agent.md")
WORKFLOW_FILE = Path("/bootstrap/opensandbox-agent-execution.bpmn")
WORKFLOW_NAME = "OpenSandbox Agent Execution"


def request_json(method: str, path: str, payload: dict | None = None):
    body = None if payload is None else json.dumps(payload).encode("utf-8")
    request = urllib.request.Request(
        f"{API_BASE}{path}",
        data=body,
        method=method,
        headers={
            "Authorization": f"Bearer {TOKEN}",
            "Content-Type": "application/json",
        },
    )

    with urllib.request.urlopen(request, timeout=30) as response:
        charset = response.headers.get_content_charset() or "utf-8"
        text = response.read().decode(charset)
        return json.loads(text) if text else None


def wait_for_api():
    deadline = time.time() + 120
    while time.time() < deadline:
        try:
            with urllib.request.urlopen(f"{API_BASE}/api/health/live", timeout=5) as response:
                if response.status == 200:
                    return
        except Exception:
            time.sleep(2)

    raise RuntimeError("Timed out waiting for Autofac API health endpoint.")


def ensure_agent():
    content = AGENT_FILE.read_text(encoding="utf-8")
    request_json(
        "POST",
        "/api/agents/upload",
        {
            "fileName": "opensandbox-pilot-agent.md",
            "content": content,
        },
    )


def ensure_workflow():
    bpmn = WORKFLOW_FILE.read_text(encoding="utf-8")
    workflows = request_json("GET", "/api/workflows") or []
    existing = next((item for item in workflows if item.get("name") == WORKFLOW_NAME), None)

    if existing is None:
        imported = request_json(
            "POST",
            "/api/workflows/import",
            {
                "fileName": "opensandbox-agent-execution.bpmn",
                "bpmnXml": bpmn,
            },
        )
        workflow_id = imported["workflowId"]
    else:
        workflow_id = existing["id"]

    request_json(
        "POST",
        f"/api/workflows/{workflow_id}/publish",
        {
            "bpmnXml": bpmn,
            "description": "Pilot workflow that proves local sandbox-backed agent execution through OpenSandbox.",
        },
    )


def main():
    wait_for_api()
    ensure_agent()
    ensure_workflow()


if __name__ == "__main__":
    try:
        main()
    except Exception as exc:
        print(f"bootstrap failed: {exc}", file=sys.stderr)
        raise
