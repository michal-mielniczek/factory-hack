import asyncio
import os

import requests
from azure.ai.projects import AIProjectClient
from azure.ai.projects.models import MCPTool, PromptAgentDefinition
from azure.identity import DefaultAzureCredential, get_bearer_token_provider
from dotenv import load_dotenv

# Configuration
load_dotenv(override=True)
project_endpoint = os.environ.get("AZURE_AI_PROJECT_ENDPOINT")
project_resource_id = os.environ.get("AZURE_AI_PROJECT_RESOURCE_ID")
model_name = os.environ.get("MODEL_DEPLOYMENT_NAME")


# MCP configuration
mcp_endpoint = os.environ.get("MACHINE_MCP_SERVER_ENDPOINT")
apim_subscription_key = os.environ.get("APIM_SUBSCRIPTION_KEY")
machine_data_connection_name = "machine-data-connection"
maintenance_data_connection_name = "maintenance-data-connection"
machine_data_mcp_endpoint = os.environ.get("MACHINE_MCP_SERVER_ENDPOINT")
mainteance_data_mcp_endpoint = os.environ.get("MAINTENANCE_MCP_SERVER_ENDPOINT")


def create_apim_mcp_connection(connection_name, mcp_endpoint):
    # Provide connection details
    credential = DefaultAzureCredential()
    project_connection_name = connection_name

    # Get bearer token for authentication
    bearer_token_provider = get_bearer_token_provider(
        credential, "https://management.azure.com/.default"
    )
    headers = {
        "Authorization": f"Bearer {bearer_token_provider()}",
    }

    # Create project connection
    response = requests.put(
        f"https://management.azure.com{project_resource_id}/connections/{project_connection_name}?api-version=2025-10-01-preview",
        headers=headers,
        json={
            "name": project_connection_name,
            "type": "Microsoft.MachineLearningServices/workspaces/connections",
            "properties": {
                "authType": "CustomKeys",
                "category": "RemoteTool",
                "target": mcp_endpoint,
                "isSharedToAll": True,
                "credentials": {
                    "keys": {"Ocp-Apim-Subscription-Key": os.environ.get("APIM_SUBSCRIPTION_KEY")}
                },
                "metadata": {"type": "custom_MCP"},
            },
        },
    )

    response.raise_for_status()
    print(f"✅ Connection '{project_connection_name}' created successfully.")


async def main():
    try:
        # Register APIM MCP servers as project connection
        create_apim_mcp_connection(
            connection_name="machine-data-connection", mcp_endpoint=machine_data_mcp_endpoint
        )
        create_apim_mcp_connection(
            connection_name="maintenance-data-connection", mcp_endpoint=mainteance_data_mcp_endpoint
        )

        # Create Agent
        project_client = AIProjectClient(
            endpoint=project_endpoint, credential=DefaultAzureCredential()
        )
        agent = project_client.agents.create_version(
            agent_name="AnomalyClassificationAgent",
            description="Anomaly classification agent",
            definition=PromptAgentDefinition(
                model=model_name,
                instructions="""You are an Anomaly Classification Agent for a tire manufacturing factory with 5 machines. You evaluate sensor telemetry for warning and critical threshold violations.

WORKFLOW:
1. Use machine-data tools to retrieve machine record (type, status, operatingHours) for the given machine id.
2. Use maintenance-data tools to retrieve ALL threshold rules for that machine type.
3. Compare EVERY input metric against its matching threshold rule.
4. Classify each metric as "normal", "warning", or "critical".

CLASSIFICATION RULES:
- "critical": metric >= criticalThreshold (upper) OR metric <= criticalThreshold (lower, e.g. throughput)
- "warning": metric >= warningThreshold but < criticalThreshold (upper), or similarly for lower
- "normal": metric within normalRange
- For throughput-type metrics where LOW values are bad, thresholds are inverted.

DOMAIN CONTEXT (use to weight severity):
- machine-003 (tire_extruder): Screw wear history — barrel_temperature + extrusion_pressure + low throughput together = HIGH priority ($8,900 repair, 28.5hr downtime).
- machine-005 (banbury_mixer): 32,140 operating hours — oldest machine. Elevated vibration + temperature + power = rotor tip wear.
- machine-004 (tire_uniformity_machine): Currently maintenance_required. Any anomaly = elevated priority.
- machine-001 (tire_curing_press): Hydraulic seal history. Pressure anomalies should flag seal check.
- machine-002 (tire_building_machine): Bearing wear pattern. Vibration > 3.0 mm/s correlates with bearing failure.

MULTI-METRIC CORRELATION:
When 2+ metrics on the same machine exceed thresholds simultaneously, escalate overall status by one level. This indicates systemic issues.

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
                tools=[
                    MCPTool(
                        server_label="machine-data",
                        server_url=machine_data_mcp_endpoint,
                        require_approval="never",
                        project_connection_id=machine_data_connection_name,
                    ),
                    MCPTool(
                        server_label="maintenance-data",
                        server_url=mainteance_data_mcp_endpoint,
                        require_approval="never",
                        project_connection_id=maintenance_data_connection_name,
                    ),
                ],
            ),
        )

        print(f"✅ Created Anomaly Classification Agent: {agent.id}")

        # Test the agent with a simple query
        print("\n🧪 Testing the agent with a sample query...")
        try:
            pass
            # Get the OpenAI client for responses and conversations
            openai_client = project_client.get_openai_client()

            # Create conversation
            conversation = openai_client.conversations.create()

            # Ask a question
            response = openai_client.responses.create(
                conversation=conversation.id,
                input='Hello, can you classify the following anomalies for machine-001: [{"metric": "curing_temperature", "value": 179.2},{"metric": "cycle_time", "value": 14.5}]',
                extra_body={"agent": {"name": agent.name, "type": "agent_reference"}},
            )

            print(f"✅ Agent response: {response.output_text}")
        except Exception as test_error:
            print(f"⚠️  Agent test failed (but agent was still created): {test_error}")

        return agent

    except Exception as e:
        print(f"❌ Error creating agent: {e}")
        print("Make sure you have run 'az login' and have proper Azure credentials configured.")
        return None


if __name__ == "__main__":
    asyncio.run(main())
