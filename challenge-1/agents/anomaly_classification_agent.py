import asyncio
import os

from agent_framework.azure import AzureAIClient
from azure.cosmos import CosmosClient
from azure.identity.aio import AzureCliCredential
from dotenv import load_dotenv

# TODO: add HostedMCPTool import

load_dotenv(override=True)

# Configuration
project_endpoint = os.environ.get("AZURE_AI_PROJECT_ENDPOINT")

# Initialize Cosmos DB clients globally for function tools
cosmos_endpoint = os.environ.get("COSMOS_ENDPOINT")
cosmos_key = os.environ.get("COSMOS_KEY")
cosmos_client = CosmosClient(cosmos_endpoint, cosmos_key)
database = cosmos_client.get_database_client("FactoryOpsDB")
thresholds_container = database.get_container_client("Thresholds")
machines_container = database.get_container_client("Machines")

# MCP configuration
# TODO: add subscription key and MCP endpoint


def get_thresholds(machine_type: str) -> list:
    """Get all thresholds for a machine type from Cosmos DB"""
    try:
        query = f"SELECT * FROM c WHERE c.machineType = '{machine_type}'"
        items = list(
            thresholds_container.query_items(query=query, enable_cross_partition_query=True)
        )
        return items
    except Exception as e:
        return [{"error": str(e)}]


def get_machine_data(machine_id: str) -> dict:
    """Get machine data from Cosmos DB"""
    try:
        query = f"SELECT * FROM c WHERE c.id = '{machine_id}'"
        items = list(machines_container.query_items(query=query, enable_cross_partition_query=True))
        return items[0] if items else {"error": f"Machine {machine_id} not found"}
    except Exception as e:
        return {"error": str(e)}


async def main():
    try:
        async with AzureCliCredential() as credential:
            async with (
                AzureAIClient(credential=credential).create_agent(
                    name="AnomalyClassificationAgent",
                    description="Anomaly classification agent",
                    instructions="""You are an Anomaly Classification Agent for a tire manufacturing factory with 5 machines. You evaluate sensor telemetry for warning and critical threshold violations.

WORKFLOW:
1. Use get_machine_data(machine_id) to retrieve the machine record (type, status, operatingHours).
2. Use get_thresholds(machine_type) to retrieve ALL threshold rules for that machine type.
3. Compare EVERY input metric against its matching threshold rule.
4. Classify each metric as "normal", "warning", or "critical".

CLASSIFICATION RULES:
- "critical": metric >= criticalThreshold (upper) OR metric <= criticalThreshold (lower, e.g. throughput)
- "warning": metric >= warningThreshold but < criticalThreshold (upper), or similarly for lower
- "normal": metric within normalRange
- For throughput-type metrics where LOW values are bad, thresholds are inverted (warningThreshold > criticalThreshold means "below warning" and "below critical").

DOMAIN CONTEXT (use to weight severity):
- machine-003 (tire_extruder): Screw wear history — barrel_temperature + extrusion_pressure + low throughput occurring together indicates likely screw degradation. This combination is HIGH priority due to $8,900 historical repair cost and 28.5hr downtime.
- machine-005 (banbury_mixer): Has 32,140 operating hours — the oldest and most-used machine. Elevated vibration + temperature + power draw together suggest rotor tip wear.
- machine-004 (tire_uniformity_machine): Currently in maintenance_required status. Any anomaly here is elevated priority.
- machine-001 (tire_curing_press): History of hydraulic seal issues. Pressure anomalies should flag seal check.
- machine-002 (tire_building_machine): Bearing wear pattern established. Vibration above 3.0 mm/s strongly correlates with bearing failure.

MULTI-METRIC CORRELATION:
When 2+ metrics on the same machine exceed thresholds simultaneously, escalate the overall status by one level (warning -> high, high -> critical). This indicates a systemic issue rather than isolated sensor noise.

OUTPUT FORMAT (strict JSON):
{
  "status": "critical" | "high" | "medium",
  "machineId": "<id>",
  "machineType": "<type>",
  "alerts": [
    {
      "name": "<metricName>",
      "severity": "critical" | "warning",
      "value": <observed_value>,
      "threshold": <threshold_value>,
      "normalRange": {"min": <min>, "max": <max>},
      "description": "<human-readable description>"
    }
  ],
  "correlatedRisks": "<description of multi-metric patterns if any>",
  "summary": {
    "totalRecordsProcessed": <int>,
    "violations": {"critical": <int>, "warning": <int>}
  }
}

After the JSON, provide a brief human-readable summary highlighting the most urgent issue.
""",
                    tools=[get_machine_data, get_thresholds],
                ) as agent,
            ):
                print(f"✅ Created Anomaly Classification Agent: {agent.id}")

                # Test the agent with a simple query
                print("\n🧪 Testing the agent with a sample query...")
                try:
                    result = await agent.run(
                        'Hello, can you classify the following anomalies for machine-001: [{"metric": "curing_temperature", "value": 179.2},{"metric": "cycle_time", "value": 14.5}]'
                    )
                    print(f"✅ Agent response: {result.text}")
                except Exception as test_error:
                    print(f"⚠️  Agent test failed (but agent was still created): {test_error}")

                return agent

    except Exception as e:
        print(f"❌ Error creating agent: {e}")
        print("Make sure you have run 'az login' and have proper Azure credentials configured.")
        return None


if __name__ == "__main__":
    asyncio.run(main())
