# Hackathon Master Execution Plan

## Executive Summary

- Shared Azure baseline: `hackathonuser47-rg` in `swedencentral`.
- Governing implementation branch: `approach_2`.
- Golden path: telemetry anomaly -> anomaly classification -> fault diagnosis -> repair planning/work order -> maintenance schedule -> parts order -> traces/UI/API verification.
- This document is the working guide for Challenge 0 through Challenge 4 execution.

## Repo Audit Summary

| Area | Status | Evidence | Required Action |
| --- | --- | --- | --- |
| Root Python tooling | Present | `pyproject.toml`, `.python-version`, `.pre-commit-config.yaml`, `uv.lock` | Keep root quality gates green for every slice |
| Challenge 0 | Partial but hardened | `challenge-0/get-keys.sh`, `challenge-0/seed-data.sh`, `.env.example` | Re-run against Azure to validate final `.env` and seeded resources |
| Challenge 1 | Partial | `challenge-1/agents/*.py` | Verify hosted agents, MCP connections, and Foundry IQ grounding |
| Challenge 2 | Missing on original branch; implemented in this execution track | `challenge-2/RepairPlanner`, `challenge-2/RepairPlanner.Tests` | Build, test, and validate work-order creation |
| Challenge 3 | Partial | `challenge-3/agents/**` | Reduce hidden fallbacks and verify persisted outputs |
| Challenge 4 | Partial | `challenge-4/agent-workflow/**` | Validate end-to-end orchestration and observability |

## Quality Gates

### Python

```bash
uv sync
uv run ruff check .
uv run ruff format --check .
uv run pytest -q
uv run pre-commit install
uv run pre-commit run --all-files
```

### Challenge 4 Python App

```bash
cd challenge-4/agent-workflow/app
uv sync
uv run pytest -q
```

### .NET

```bash
dotnet restore factory-hack.sln
dotnet build challenge-2/RepairPlanner/RepairPlanner.csproj --configuration Release
dotnet test challenge-2/RepairPlanner.Tests/RepairPlanner.Tests.csproj --configuration Release
dotnet build challenge-4/agent-workflow/dotnetworkflow/FactoryWorkflow.csproj --configuration Release
```

### Frontend

```bash
cd challenge-4/agent-workflow/frontend
npm ci
npm run lint
npm run build
```

## Execution Order

1. Generate and verify `.env` from Challenge 0 using the shared resource group.
2. Seed Cosmos DB, blob storage, and APIM artifacts from Challenge 0.
3. Register and validate Challenge 1 hosted agents with MCP and Foundry IQ.
4. Build and validate Challenge 2 repair planning and work-order creation.
5. Verify Challenge 3 schedules, parts orders, and chat-history persistence.
6. Run the full Challenge 4 flow through Aspire and keep that path green after every push.

## Acceptance Criteria by Challenge

### Challenge 0

- `.env` contains canonical Azure AI, Cosmos, Search, APIM, and observability values.
- Seed script creates all documented containers, including `Suppliers`, `MaintenanceSchedules`, `PartsOrders`, and `ChatHistories`.
- Blob wiki content is uploaded and APIM seed step succeeds or is explicitly marked unverified.

### Challenge 1

- `AnomalyClassificationAgent` is created and reachable.
- `FaultDiagnosisAgent` is created with both `machine-data` and `machine-wiki` MCP tools.
- Fault diagnosis output stays grounded to machine data and Foundry IQ content.

### Challenge 2

- .NET repair planner resolves technicians and parts from Cosmos DB.
- Structured work order output is normalized and persisted.
- Unit tests cover fault mappings and technician selection behavior.

### Challenge 3

- Scheduler writes `MaintenanceSchedules`.
- Parts ordering writes `PartsOrders`.
- Persistent memory writes `ChatHistories`.
- Any unverified fallback path is explicitly documented.

### Challenge 4

- Frontend, .NET workflow host, and Python A2A services run together.
- Workflow response preserves `agentSteps` and `finalMessage`.
- Traces/logs can be correlated across the end-to-end execution.

## Definition of Done

- Every code change is reviewed.
- Every meaningful component has tests or an explicit documented gap.
- Every integration point is validated.
- Outputs are verified through code, stored data, traces, or logs.
- No challenge is complete until lint, relevant tests, and the intended smoke/integration checks pass.

## Current Risks

- Azure verification remains dependent on available authenticated tooling.
- Full .NET validation is still pending in an environment with the SDK installed.
- Challenge 3 still contains fallback behavior that can mask missing data if not tightened further.
