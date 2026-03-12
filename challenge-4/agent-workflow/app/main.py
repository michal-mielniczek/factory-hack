import contextlib
import datetime
import json
import logging
import os
import random

import fastapi
import fastapi.responses
import fastapi.staticfiles
import opentelemetry.instrumentation.fastapi as otel_fastapi
import telemetry
from pydantic import BaseModel
from agents import (
    run_factory_workflow,
    create_maintenance_scheduler_a2a_app,
    create_parts_ordering_a2a_app,
)
from agent_framework.observability import configure_otel_providers
from dotenv import load_dotenv
from factory_data import (
    build_dashboard_summary,
    compute_risk_scores,
    compute_inventory_forecast,
    get_machines,
    get_inventory,
    get_work_orders,
    get_thresholds,
    get_maintenance_history,
    get_telemetry_samples,
)


@contextlib.asynccontextmanager
async def lifespan(app):
    telemetry.configure_opentelemetry()
    configure_otel_providers()
    yield


# Configure logging
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)

load_dotenv()

app = fastapi.FastAPI(lifespan=lifespan)


# Add middleware to log all requests
@app.middleware("http")
async def log_requests(request: fastapi.Request, call_next):
    logger.info(f">>> Incoming request: {request.method} {request.url.path}")
    response = await call_next(request)
    logger.info(
        f"<<< Response: {request.method} {request.url.path} - Status: {response.status_code}"
    )
    return response


otel_fastapi.FastAPIInstrumentor.instrument_app(app, exclude_spans=["send"])

# =============================================================================
# A2A Agent Endpoints
# Mount the A2A Starlette applications for the challenge-3 agents
# These can be called from the dotnet workflow using A2A protocol
# =============================================================================
try:
    maintenance_scheduler_a2a = create_maintenance_scheduler_a2a_app()
    parts_ordering_a2a = create_parts_ordering_a2a_app()

    # Build the Starlette apps from A2A applications and mount them
    maintenance_scheduler_starlette = maintenance_scheduler_a2a.build()
    parts_ordering_starlette = parts_ordering_a2a.build()

    app.mount("/maintenance-scheduler", maintenance_scheduler_starlette)
    app.mount("/parts-ordering", parts_ordering_starlette)
    logger.info("A2A agents mounted successfully at /maintenance-scheduler and /parts-ordering")
except Exception as e:
    logger.warning(f"Failed to initialize A2A agents: {e}")


if not os.path.exists("static"):

    @app.get("/", response_class=fastapi.responses.HTMLResponse)
    async def root():
        """Root endpoint."""
        return "API service is running. Navigate to <a href='/api/weatherforecast'>/api/weatherforecast</a> to see sample data or POST to <a href='/docs'>/api/analyze_machine</a>."


@app.get("/api/weatherforecast")
async def weather_forecast():
    """Weather forecast endpoint."""
    # Generate fresh data if not in cache or cache unavailable.
    summaries = [
        "Freezing",
        "Bracing",
        "Chilly",
        "Cool",
        "Mild",
        "Warm",
        "Balmy",
        "Hot",
        "Sweltering",
        "Scorching",
    ]

    forecast = []
    for index in range(1, 6):  # Range 1 to 5 (inclusive)
        temp_c = random.randint(-20, 55)
        forecast_date = datetime.datetime.now() + datetime.timedelta(days=index)
        forecast_item = {
            "date": forecast_date.isoformat(),
            "temperatureC": temp_c,
            "temperatureF": int(temp_c * 9 / 5) + 32,
            "summary": random.choice(summaries),
        }
        forecast.append(forecast_item)

    return forecast


class AnalyzeRequest(BaseModel):
    machine_id: str
    telemetry: list[dict] | dict


@app.post("/api/analyze_machine")
async def analyze_machine(request: AnalyzeRequest):
    logger.info(f"Analyzing machine {request.machine_id}")

    try:
        outputs = await run_factory_workflow(request.machine_id, request.telemetry)

        serialized_outputs = []
        for out in outputs:
            # Handle AgentRunResponse or similar
            if hasattr(out, "text"):
                serialized_outputs.append(out.text)
            elif hasattr(out, "params") and hasattr(
                out.params, "text"
            ):  # AgentRunResponse vs AgentRunEvent
                serialized_outputs.append(str(out))
            else:
                serialized_outputs.append(str(out))

        return {"results": serialized_outputs}

    except Exception as e:
        logger.error(f"Workflow failed: {e}")
        return fastapi.responses.JSONResponse(content={"error": str(e)}, status_code=500)


@app.get("/health", response_class=fastapi.responses.PlainTextResponse)
async def health_check():
    """Health check endpoint."""
    return "Healthy"


# =============================================================================
# Factory Dashboard API Endpoints
# =============================================================================


@app.get("/api/factory/dashboard")
async def factory_dashboard():
    """Aggregated factory dashboard: machines, inventory, work orders, KPIs."""
    return build_dashboard_summary()


@app.get("/api/factory/risk-scores")
async def factory_risk_scores():
    """Predictive maintenance risk scores per machine."""
    return compute_risk_scores()


@app.get("/api/factory/inventory-forecast")
async def factory_inventory_forecast():
    """Inventory depletion forecast and reorder urgency."""
    return compute_inventory_forecast()


@app.get("/api/factory/machines")
async def list_machines():
    """List all machines with details."""
    return get_machines()


@app.get("/api/factory/telemetry-samples")
async def list_telemetry_samples():
    """List all telemetry samples."""
    return get_telemetry_samples()


# =============================================================================
# Factory Chat Agent Endpoint
# =============================================================================


class ChatRequest(BaseModel):
    question: str


@app.post("/api/chat")
async def factory_chat(request: ChatRequest):
    """Interactive chat about factory operations using GPT-4.1."""
    from openai import AsyncAzureOpenAI

    endpoint = os.getenv("AZURE_OPENAI_ENDPOINT") or os.getenv("APIM_GATEWAY_URL")
    api_key = os.getenv("AZURE_OPENAI_KEY") or os.getenv("APIM_SUBSCRIPTION_KEY")
    deployment = os.getenv("MODEL_DEPLOYMENT_NAME", "gpt-4.1")

    if not endpoint:
        return fastapi.responses.JSONResponse(
            content={"error": "AZURE_OPENAI_ENDPOINT not configured"}, status_code=500
        )

    # Build context from factory data
    machines = get_machines()
    inventory = get_inventory()
    work_orders = get_work_orders()
    thresholds = get_thresholds()
    history = get_maintenance_history()
    risk_scores = compute_risk_scores()

    context = (
        f"Factory Data Context:\n"
        f"- {len(machines)} machines: {', '.join(m['name'] for m in machines)}\n"
        f"- {len(inventory)} parts in inventory\n"
        f"- {len(work_orders)} work orders\n"
        f"- {len(history)} maintenance history records\n\n"
        f"Machines:\n{json.dumps(machines, indent=2, default=str)[:3000]}\n\n"
        f"Inventory (summary):\n{json.dumps([{'name': p['name'], 'stock': p.get('quantityInStock'), 'reorder': p.get('reorderLevel')} for p in inventory], indent=2)}\n\n"
        f"Risk Scores:\n{json.dumps(risk_scores, indent=2, default=str)[:2000]}\n\n"
        f"Thresholds:\n{json.dumps(thresholds, indent=2)[:1500]}\n\n"
        f"Recent Work Orders:\n{json.dumps(work_orders[:5], indent=2, default=str)[:2000]}\n"
    )

    client_kwargs = {"api_version": "2024-12-01-preview"}
    if api_key:
        client_kwargs["api_key"] = api_key
    else:
        from azure.identity.aio import DefaultAzureCredential as AsyncCred

        client_kwargs["azure_ad_token_provider"] = AsyncCred()

    client = AsyncAzureOpenAI(azure_endpoint=endpoint, **client_kwargs)

    try:
        response = await client.chat.completions.create(
            model=deployment,
            messages=[
                {
                    "role": "system",
                    "content": (
                        "You are a factory operations assistant. Answer questions about machines, "
                        "inventory, maintenance, work orders, and risk scores using the provided data. "
                        "Be concise and helpful. Use specific numbers from the data.\n\n" + context
                    ),
                },
                {"role": "user", "content": request.question},
            ],
            max_tokens=1000,
            temperature=0.3,
        )
        answer = response.choices[0].message.content
        return {"question": request.question, "answer": answer}
    except Exception as e:
        logger.exception("Chat endpoint error")
        return fastapi.responses.JSONResponse(
            content={"error": f"Chat failed: {str(e)}"}, status_code=500
        )
    finally:
        await client.close()


# =============================================================================
# Multi-Machine Simulation Engine
# =============================================================================


class SimulationRequest(BaseModel):
    steps: int = 10  # Number of time steps to simulate
    degradation_rate: float = 0.02  # How fast machines degrade per step


@app.post("/api/factory/simulate")
async def factory_simulate(request: SimulationRequest):
    """Simulate factory operations over time.

    Generates telemetry for all machines, applies degradation, detects anomalies.
    """
    machines = get_machines()
    thresholds = get_thresholds()
    inventory = get_inventory()

    # Build threshold lookup
    threshold_map: dict[str, list[dict]] = {}
    for t in thresholds:
        mt = t["machineType"]
        if mt not in threshold_map:
            threshold_map[mt] = []
        threshold_map[mt].append(t)

    timeline: list[dict] = []
    # Track machine health as a multiplier (1.0 = perfect, >1.0 = degraded)
    machine_health = {m["id"]: 1.0 for m in machines}
    # Track inventory consumption
    inventory_levels = {p["id"]: p.get("quantityInStock", 0) for p in inventory}

    anomaly_count = 0
    repair_count = 0

    for step in range(request.steps):
        step_events: list[dict] = []

        for m in machines:
            mid = m["id"]
            mtype = m["type"]
            health = machine_health[mid]

            # Get thresholds for this machine type
            machine_thresholds = threshold_map.get(mtype, [])
            if not machine_thresholds:
                continue

            # Generate telemetry based on health factor
            telemetry: dict[str, float] = {}
            alerts: list[dict] = []

            for t in machine_thresholds:
                metric = t["metric"]
                normal_range = t.get("normalRange", {})
                normal_mid = (normal_range.get("min", 0) + normal_range.get("max", 0)) / 2
                normal_spread = (normal_range.get("max", 0) - normal_range.get("min", 0)) / 2
                warning = t.get("warningThreshold", 0)
                critical = t.get("criticalThreshold", 0)

                # Value drifts with health degradation
                import random

                noise = random.gauss(0, normal_spread * 0.1)
                value = normal_mid + (health - 1.0) * (critical - normal_mid) * 3 + noise
                telemetry[metric] = round(value, 2)

                # Check thresholds
                if abs(value) >= abs(critical):
                    alerts.append(
                        {
                            "metric": metric,
                            "value": round(value, 2),
                            "severity": "critical",
                            "threshold": critical,
                        }
                    )
                elif abs(value) >= abs(warning):
                    alerts.append(
                        {
                            "metric": metric,
                            "value": round(value, 2),
                            "severity": "warning",
                            "threshold": warning,
                        }
                    )

            # Determine if anomaly detected
            is_anomaly = len(alerts) > 0
            if is_anomaly:
                anomaly_count += 1

            # If critical, simulate repair (consume parts, reset health)
            repaired = False
            critical_alerts = [a for a in alerts if a["severity"] == "critical"]
            if critical_alerts:
                # Consume a compatible part
                compatible = [p for p in inventory if mtype in p.get("compatibleMachines", [])]
                for cp in compatible:
                    if inventory_levels.get(cp["id"], 0) > 0:
                        inventory_levels[cp["id"]] -= 1
                        repaired = True
                        repair_count += 1
                        machine_health[mid] = max(1.0, health * 0.5)  # Partial recovery
                        break

                if not repaired:
                    # No parts available - machine keeps degrading faster
                    machine_health[mid] = health + request.degradation_rate * 2

            # Apply degradation
            machine_health[mid] += request.degradation_rate * random.uniform(0.5, 1.5)

            step_events.append(
                {
                    "machineId": mid,
                    "machineName": m["name"],
                    "healthFactor": round(machine_health[mid], 3),
                    "telemetry": telemetry,
                    "alerts": alerts,
                    "isAnomaly": is_anomaly,
                    "repaired": repaired,
                }
            )

        timeline.append(
            {
                "step": step,
                "events": step_events,
            }
        )

    # Final inventory snapshot
    inventory_final = []
    for p in inventory:
        original = p.get("quantityInStock", 0)
        current = inventory_levels.get(p["id"], 0)
        inventory_final.append(
            {
                "partId": p["id"],
                "name": p["name"],
                "originalStock": original,
                "currentStock": current,
                "consumed": original - current,
            }
        )

    return {
        "steps": request.steps,
        "degradationRate": request.degradation_rate,
        "summary": {
            "totalAnomalies": anomaly_count,
            "totalRepairs": repair_count,
            "machinesAtRisk": sum(1 for h in machine_health.values() if h > 1.5),
        },
        "machineHealth": {mid: round(h, 3) for mid, h in machine_health.items()},
        "inventoryStatus": inventory_final,
        "timeline": timeline,
    }


# Serve static files directly from root, if the "static" directory exists
if os.path.exists("static"):
    app.mount("/", fastapi.staticfiles.StaticFiles(directory="static", html=True), name="static")
