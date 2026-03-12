# Hackathon Master Plan

## Executive Summary
- Use the shared Azure environment in `hackathonuser47-rg` in `swedencentral` as the default baseline.
- Treat this document as the execution contract for the day: no challenge is done until its code, integration points, and outputs are verified.
- Keep one golden path working throughout the day: seeded anomaly input -> hosted anomaly classification -> hosted fault diagnosis -> real repair planning -> real maintenance scheduling -> real parts decision -> visible UI/API/traces.
- Work in small reviewable slices on branch `approach_2`, push frequently, and address review findings before building the next layer.

## Repo Audit Summary
- The repo already contains substantive starter or partial implementation for Challenges 0, 1, 3, and 4.
- Challenge 2 currently has guidance plus an off-limits `example-solution`, but no exercise project at `challenge-2/RepairPlanner`.
- There are no repo-wide automated tests, no repo-root Python project metadata, no repo-root pre-commit config, and no CI workflow.
- Current shell readiness is incomplete:
  - Present: `az`, `uv`, `ruff`, `pytest`, `node`, `npm`
  - Missing from current shell: `pre-commit`, `jq`, `dotnet`, `aspire`
  - Missing locally: repo-root `.env`
- Challenge 0 Task 4 and Task 7 are verified complete in Azure:
  - Shared resource group exists with Foundry, Cosmos DB, APIM, Search, App Insights, ACR, and Container Apps resources.
  - Current user has `Azure AI Developer` and `Cognitive Services OpenAI Contributor` at resource-group scope.

## Challenge Status
| Challenge | Outcome | Current Status | Evidence | Main Gaps |
| --- | --- | --- | --- | --- |
| 0 | Shared cloud/data foundation | Verified in Azure, partial locally | Shared RG/resources, seeded containers/APIs/blob container present | No local `.env`; scripts not verified from this checkout |
| 1 | Hosted anomaly classification and fault diagnosis | Partial | Python agents exist, MCP anomaly path exists | Foundry IQ MCP tool still missing in fault diagnosis; local MCP/KB setup unverified |
| 2 | Real .NET Repair Planner agent | Missing as exercise project | Guidance and custom Copilot agent exist | Need new project, tests, and integration contract |
| 3 | Scheduler and parts-ordering agents with memory/tracing | Mostly implemented | Agents, Cosmos service, chat history, observability helpers exist | Data-contract gaps, hidden mock fallbacks, no tests |
| 4 | End-to-end workflow in Aspire | Mostly implemented, not fully verified | Aspire host, .NET API, Python A2A app, frontend exist | Current shell missing .NET; integrated repair planner still uses mock tech/parts behavior |

## Assumptions And Open Questions
- Assumption: the shared RG stays available for the full hackathon day.
- Assumption: branch `approach_2` is the working branch for implementation and review.
- Assumption: `challenge-2/example-solution/**` stays off-limits unless explicitly approved later.
- Open question to record during execution: whether all required Foundry agents, MCP servers, and knowledge-base connections already exist in the shared environment or must be recreated from this checkout.
- Open question to record during execution: whether local shell bootstrap should be fixed in-place or whether the team should standardize on Codespaces/devcontainer for all hands-on work.

## Architecture Direction
- Azure-first, multi-agent system with explicit boundaries between hosted Foundry agents, local agent services, and orchestration.
- Hosted Foundry agents:
  - `AnomalyClassificationAgent`
  - `FaultDiagnosisAgent`
- Local agents:
  - Challenge 2 .NET `RepairPlanner`
  - Challenge 3 Python `MaintenanceSchedulerAgent`
  - Challenge 3 Python `PartsOrderingAgent`
- Orchestration:
  - Challenge 4 .NET workflow host is the single golden-path orchestrator.
  - Challenge 4 Python app exists to host the Challenge 3 A2A endpoints, not to become a second orchestration path.
- Shared data and operations:
  - Cosmos DB is the authoritative operational store.
  - APIM/MCP and Foundry IQ are the governed tool/knowledge interfaces for hosted agents.
  - App Insights / OpenTelemetry traces must be treated as required evidence, not optional diagnostics.

## Execution Order
1. Bootstrap the repo and quality gates.
2. Align Challenge 0 local setup with the verified shared Azure environment.
3. Finish and verify Challenge 1 hosted agents and their MCP/knowledge connections.
4. Build Challenge 2 as a real .NET project with tests.
5. Harden Challenge 3 against seeded data and remove silent fallback behavior.
6. Integrate the real Challenge 2 planner into Challenge 4 and verify the full workflow.
7. Keep the golden path green after each slice and push after each verified slice.

## Challenge-By-Challenge Plan

### Challenge 0
**Goal**
- Make the shared Azure baseline reproducible and verifiable from this checkout.

**Implementation**
- Generate a repo-root `.env` from the shared RG using `challenge-0/get-keys.sh`.
- Normalize canonical env vars across docs, `.env.example`, scripts, and code:
  - `AZURE_AI_PROJECT_ENDPOINT`
  - `MODEL_DEPLOYMENT_NAME`
  - `COSMOS_ENDPOINT`
  - `COSMOS_KEY`
  - `COSMOS_DATABASE_NAME`
  - `APPLICATIONINSIGHTS_CONNECTION_STRING`
- Align `seed-data.sh` and the documented data contract so later challenges do not depend on undocumented data.
- Seed `Suppliers` explicitly and document which output containers are intentionally runtime-created.

**Verification**
- Prove `.env` generation from the shared RG.
- Prove Cosmos database/container reachability.
- Prove APIM API reachability.
- Prove blob container presence and machine wiki availability.

### Challenge 1
**Goal**
- Have two working hosted Foundry agents with governed MCP and knowledge-base access.

**Implementation**
- Complete the Foundry IQ MCP integration in `fault_diagnosis_agent.py`.
- Verify APIM MCP servers for machine and maintenance access.
- Verify project connections and agent registration in Foundry.
- Keep the direct-Cosmos anomaly script as a fallback smoke path only.

**Verification**
- Run anomaly classification against seeded warning input and confirm MCP-backed output.
- Run fault diagnosis against seeded anomaly input and confirm the response is grounded in the knowledge base.
- Verify agent registrations and connection objects in the shared Foundry project.

### Challenge 2
**Goal**
- Create a real .NET `RepairPlanner` project for the exercise path.

**Implementation**
- Create `challenge-2/RepairPlanner`.
- Implement:
  - data models
  - fault-to-skills and fault-to-parts mapping service
  - Cosmos DB service
  - planner orchestration and output validation
- Add unit tests for mapping and Cosmos-facing behavior.
- Expose a reusable interface so Challenge 4 can consume the same planner logic.

**Verification**
- Build and test the project locally.
- Create a real work order in Cosmos from a diagnosed fault input.
- Verify work-order shape and required parts/technician selection behavior.

### Challenge 3
**Goal**
- Make scheduler and parts ordering behave against real seeded data, with durable outputs and observable traces.

**Implementation**
- Validate both agents against seeded work orders instead of relying on mock suppliers.
- Treat `ChatHistories`, `MaintenanceSchedules`, and `PartsOrders` as required outputs.
- Tighten fallback behavior so missing data surfaces clearly rather than silently degrading.
- Require App Insights tracing to be configured and emitted.

**Verification**
- Confirm maintenance schedules are written to Cosmos.
- Confirm parts orders are written when inventory requires ordering.
- Confirm chat histories are written and reused.
- Confirm traces are visible for agent runs and data access.

### Challenge 4
**Goal**
- Run one real, end-to-end, observable workflow through the UI and API.

**Implementation**
- Keep the .NET workflow host as the single orchestrator.
- Replace mock repair-planner technician/parts behavior with the real Challenge 2 implementation.
- Keep the Python app focused on hosting A2A endpoints for Challenge 3 agents.
- Preserve the existing `WorkflowResponse` contract: `agentSteps`, `finalMessage`.

**Verification**
- `aspire run` brings up:
  - frontend
  - .NET workflow API
  - Python A2A endpoints
  - dashboard
- Trigger one seeded anomaly from the UI.
- Confirm step-by-step workflow output, Cosmos writes, and traces line up for the same run.

## Testing Framework

### Unit Tests
- Challenge 2 mapping service and planner validation.
- Challenge 2 Cosmos service query/write behavior with isolated test cases.
- Challenge 3 shared data/service functions where deterministic logic exists.
- Any new parsing or transformation logic added in Challenge 1 and Challenge 4.

### Integration Tests
- Challenge 0 scripts against the shared RG.
- Challenge 1 hosted agents with MCP tools and knowledge-base access.
- Challenge 2 work-order creation in Cosmos.
- Challenge 3 schedule/order/history writes in Cosmos.
- Challenge 4 workflow API contract and end-to-end orchestration.

### Smoke Tests
- Seeded anomaly path for `machine-001`.
- One non-anomalous classification path.
- One missing or invalid machine path.
- One workflow path requiring parts ordering and one path that should become `Ready`.

## Output Verification Framework
- Verify by evidence, not by logs alone.
- For every challenge, collect at least one of:
  - returned API payload
  - stored Cosmos document
  - Azure resource/config state
  - trace/span evidence
  - tool-call evidence
- Mark any unverified behavior explicitly as `unverified`.
- Do not close a challenge on “it probably worked.”

## Observability And Traceability Plan
- App Insights tracing is mandatory for meaningful completion of Challenge 3 and Challenge 4.
- Use a shared traceability checklist per run:
  - workflow request received
  - each agent invoked
  - tool/connection access observed
  - final output returned
  - persistent documents written
- Aspire dashboard is the first local cross-service view.
- Foundry traces are the first hosted-agent debugging surface.

## Quality Gates And Definition Of Done
- Every code change must be reviewed.
- Every meaningful component must have tests.
- Every challenge must have explicit acceptance criteria.
- Every integration point must be validated.
- End-to-end checks must run continuously during development, not just at the end.
- No feature is done unless lint, relevant tests, and smoke/integration verification pass.

## Environment And Bootstrap Plan

### Required Tooling
- Python via `uv`
- `ruff`
- `pytest`
- `pre-commit`
- `jq`
- .NET SDK 10
- Aspire CLI
- Node/NPM for the frontend

### Canonical Local Workflow
```bash
uv sync
uv run ruff check .
uv run ruff format --check .
uv run pytest -q
uv run pre-commit install
uv run pre-commit run --all-files
```

### Challenge 4 Nested Python App
```bash
cd challenge-4/agent-workflow/app
uv sync
uv run pytest -q
```

### .NET Workflow
```bash
dotnet restore factory-hack.sln
dotnet build factory-hack.sln
dotnet build challenge-2/RepairPlanner/RepairPlanner.csproj
dotnet format factory-hack.sln --verify-no-changes
dotnet test
```

### Frontend Workflow
```bash
cd challenge-4/agent-workflow/frontend
npm ci
npm run lint
npm run build
```

### Shared Azure Verification Commands
```bash
./challenge-0/get-keys.sh --resource-group hackathonuser47-rg
export $(cat .env | xargs)
az cosmosdb sql container list --account-name "$COSMOS_NAME" --resource-group "$RESOURCE_GROUP" --database-name "$COSMOS_DATABASE_NAME"
curl -fsSL "$APIM_GATEWAY_URL/machine" -H "Ocp-Apim-Subscription-Key: $APIM_SUBSCRIPTION_KEY"
```

## Tooling And Quality Enforcement Plan
- Add repo-root `pyproject.toml`, `.python-version`, `uv.lock`, and `.pre-commit-config.yaml`.
- Keep `challenge-4/agent-workflow/app` as a separate nested `uv` project targeting Python 3.13.
- Standardize linting and formatting on Ruff for repo-root Python code.
- Standardize local verification commands in docs and pre-commit hooks.
- Add tests before broad refactors so later integration work has a safety net.

## Risk Register
| Risk | Impact | Mitigation |
| --- | --- | --- |
| Shared Azure environment drifts or disappears | Blocks all dependent challenges | Keep fresh deployment as documented fallback; re-run Challenge 0 verification early |
| Missing local bootstrap (`dotnet`, `aspire`, `jq`, `pre-commit`) | Prevents verification and integration work | Normalize on devcontainer/Codespaces or install missing tools before feature work |
| Challenge 1 hosted assets do not exist or mismatch code | Blocks Challenge 4 | Recreate MCP/project connection/agent assets early and verify immediately |
| Challenge 3 fallback behavior hides broken data contracts | Produces false green runs | Remove or narrow fallbacks and require evidence from Cosmos/traces |
| Challenge 4 continues to use mock repair-planner data | Golden path is not real | Swap in the real Challenge 2 planner before claiming end-to-end completion |
| Review debt accumulates | Bugs survive until late | Push small slices, resolve Copilot review findings immediately |

## Per-Challenge Checklists

### Challenge 0 Checklist
- [ ] Local `.env` generated from shared RG
- [ ] Canonical env vars align across docs/scripts/code
- [ ] Cosmos/Blob/APIM verified from this checkout
- [ ] Seed/data contract updated and documented

### Challenge 1 Checklist
- [ ] Fault diagnosis knowledge-base MCP tool implemented
- [ ] MCP servers verified
- [ ] Project connections verified
- [ ] Hosted agents registered and runnable

### Challenge 2 Checklist
- [ ] `challenge-2/RepairPlanner` created
- [ ] Planner logic implemented
- [ ] Unit tests added and passing
- [ ] Real work order written to Cosmos

### Challenge 3 Checklist
- [ ] Scheduler verified against seeded work order
- [ ] Parts ordering verified against seeded work order
- [ ] Required output containers populated
- [ ] Traces visible in App Insights / Foundry

### Challenge 4 Checklist
- [ ] Real Repair Planner integrated
- [ ] .NET workflow host verified
- [ ] Python A2A endpoints verified
- [ ] Frontend/API/dashboard full run verified

## Golden Path Criteria
- Input comes from a seeded anomaly scenario.
- Hosted anomaly classification produces actionable alert output.
- Hosted fault diagnosis returns grounded, knowledge-backed fault output.
- Real repair planner creates a real work order in Cosmos.
- Scheduler and parts ordering produce persistent outputs in Cosmos.
- UI, API response, dashboard logs, and traces all describe the same run.
- Any break in this path is a stop-the-line issue until the path is green again.
