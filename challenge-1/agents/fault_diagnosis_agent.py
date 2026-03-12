import asyncio
import os

import requests
from azure.ai.projects import AIProjectClient
from azure.ai.projects.models import MCPTool, PromptAgentDefinition
from azure.identity import DefaultAzureCredential, get_bearer_token_provider
from dotenv import load_dotenv

load_dotenv(override=True)
project_endpoint = os.environ.get("AZURE_AI_PROJECT_ENDPOINT")
project_resource_id = os.environ.get("AZURE_AI_PROJECT_RESOURCE_ID")
model_name = os.environ.get("MODEL_DEPLOYMENT_NAME")

# Configuration
knowledge_base_name = "machine-kb"
search_endpoint = os.environ.get("SEARCH_SERVICE_ENDPOINT")
machine_wiki_mcp_endpoint = f"{search_endpoint.rstrip('/')}/knowledgebases/{knowledge_base_name}/mcp?api-version=2025-11-01-preview"
machine_data_mcp_endpoint = os.environ.get("MACHINE_MCP_SERVER_ENDPOINT")
apim_subscription_key = os.environ.get("APIM_SUBSCRIPTION_KEY")


def create_project_connection(
    connection_name: str,
    target: str,
    auth_type: str,
    credentials=None,
    metadata=None,
    audience=None,
):
    bearer_token_provider = get_bearer_token_provider(
        DefaultAzureCredential(), "https://management.azure.com/.default"
    )
    headers = {"Authorization": f"Bearer {bearer_token_provider()}"}

    properties = {
        "authType": auth_type,
        "category": "RemoteTool",
        "target": target,
        "isSharedToAll": True,
    }
    if credentials:
        properties["credentials"] = credentials
    if metadata:
        properties["metadata"] = metadata
    if audience:
        properties["audience"] = audience

    response = requests.put(
        f"https://management.azure.com{project_resource_id}/connections/{connection_name}?api-version=2025-10-01-preview",
        headers=headers,
        json={
            "name": connection_name,
            "type": "Microsoft.MachineLearningServices/workspaces/connections",
            "properties": properties,
        },
    )
    response.raise_for_status()
    print(f"✅ Connection '{connection_name}' created successfully.")


async def main():
    try:
        if machine_data_mcp_endpoint:
            create_project_connection(
                connection_name="machine-data-connection",
                target=machine_data_mcp_endpoint,
                auth_type="CustomKeys",
                credentials={"keys": {"Ocp-Apim-Subscription-Key": apim_subscription_key}},
                metadata={"type": "custom_MCP"},
            )
        else:
            print("⚠️  MACHINE_MCP_SERVER_ENDPOINT not set — skipping machine-data MCP connection")
        create_project_connection(
            connection_name="machine-wiki-connection",
            target=machine_wiki_mcp_endpoint,
            auth_type="ProjectManagedIdentity",
            audience="https://search.azure.com/",
            metadata={"ApiType": "Azure"},
        )

        tools = [
            MCPTool(
                server_label="machine-wiki",
                server_url=machine_wiki_mcp_endpoint,
                require_approval="never",
                project_connection_id="machine-wiki-connection",
            ),
        ]
        if machine_data_mcp_endpoint:
            tools.insert(
                0,
                MCPTool(
                    server_label="machine-data",
                    server_url=machine_data_mcp_endpoint,
                    require_approval="never",
                    project_connection_id="machine-data-connection",
                ),
            )

        project_client = AIProjectClient(
            endpoint=project_endpoint, credential=DefaultAzureCredential()
        )
        agent = project_client.agents.create_version(
            agent_name="FaultDiagnosisAgent",
            description="Fault diagnosis agent",
            definition=PromptAgentDefinition(
                model=model_name,
                instructions="""You are a Fault Diagnosis Agent for a tire manufacturing factory. You determine root causes of maintenance alerts using knowledge base articles and machine data.

You will receive detected sensor deviations from the Anomaly Classification Agent. Your task is to determine the most likely root cause using ONLY the provided tools.

WORKFLOW:
1. Parse the input to identify the machine ID and the anomalous metrics/values.
2. Use machine-data tools to get the machine record: type, status, operatingHours, maintenanceHistory.
3. Use machine-wiki tools to search for knowledge base articles matching the fault symptoms.
4. Cross-reference the machine's maintenance history with the KB article's possibleCauses to identify recurring patterns.
5. Select the most likely root cause based on: (a) KB match quality, (b) maintenance history corroboration, (c) operating hour wear expectations.

DIAGNOSIS HEURISTICS (from historical data analysis):
- machine-001 (tire_curing_press, 12,450 hrs): 3 prior faults — temp sensor failure, hydraulic seal leak, pressure relief valve. If temperature anomaly → check sensor calibration first (most common). If pressure anomaly → check hydraulic seals (highest cost: $3,200).
- machine-002 (tire_building_machine, 18,920 hrs): 3 prior faults — bearing wear, tension sensor, pneumatic leak. Vibration > 3.0 mm/s strongly indicates bearing wear (confirmed by history). This machine has had bearing replacement before — re-occurrence suggests accelerated wear.
- machine-003 (tire_extruder, 15,230 hrs): 2 prior faults — motor overheating ($5,200) and screw wear ($8,900, 28.5hr downtime). Multiple simultaneous anomalies (temp + pressure + throughput) strongly indicate screw wear. This is the HIGHEST COST failure mode in the factory.
- machine-004 (tire_uniformity_machine, 24,560 hrs, STATUS: maintenance_required): 2 prior faults — load cell drift and spindle bearing seizure. Currently not fully operational. Load cell issues may mask real problems. Spindle bearing failure cost $4,500 with 20.5hr downtime.
- machine-005 (banbury_mixer, 32,140 hrs — OLDEST): 2 prior faults (minor: vision system alignment, conveyor belt). But high operating hours mean rotor tip wear is likely. The temperature + power + vibration combination matches "mixing_temperature_excessive" KB article.

SEVERITY DETERMINATION:
- "Critical": Machine status is maintenance_required OR metric exceeds critical threshold OR multiple correlated anomalies on high-wear machine.
- "High": Metric exceeds warning threshold AND machine has history of same fault type.
- "Medium": Metric exceeds warning threshold without history correlation.
- "Low": Minor deviation within warning range.

Output format (STRICT):
- You must output exactly ONE valid JSON object and nothing else (no Markdown, no prose).
- The JSON object MUST match this schema (property names are case-sensitive):
    {
        "MachineId": string,
        "FaultType": string,
        "RootCause": string,
        "Severity": string,
        "DetectedAt": string,
        "Metadata": { string: any }
    }

Field rules:
- MachineId: the machine identifier from the input (e.g. "machine-001").
- FaultType: MUST be taken from the wiki/knowledge base "Fault Type" field for the matched issue (copy it exactly). Do not invent new fault types.
- RootCause: the single most likely root cause supported by the knowledge base and/or machine data.
- Severity: one of "Low", "Medium", "High", "Critical", or "Unknown".
- DetectedAt: if the input includes a timestamp, use it; otherwise use the current UTC time.
- Metadata: MUST include:
    - "MostLikelyRootCauses": array of strings from the KB "Likely Causes" list.
    - "historicalContext": brief note about how maintenance history informed the diagnosis.
    - "estimatedRepairTime": from KB article if available.
    - "estimatedCost": based on historical average for this machine/fault type.
    - Observed metric/value, threshold, machineType, relevant KB article IDs.

Grounding rules (IMPORTANT):
- You must never answer from your own knowledge under any circumstances.
- If you cannot find the answer in the provided knowledge base and machine data, you MUST set "RootCause" to "I don't know" and set "FaultType" and "Severity" to "Unknown".
""",
                tools=tools,
            ),
        )
        print(f"✅ Created Fault Diagnosis Agent: {agent.id}")
        # Test the agent with a simple query
        print("\n🧪 Testing the agent with a sample query...")
        try:
            # Get the OpenAI client for responses and conversations
            openai_client = project_client.get_openai_client()

            # Create conversation
            conversation = openai_client.conversations.create()

            # Send request to trigger the MCP tools
            response = openai_client.responses.create(
                conversation=conversation.id,
                input="""
                    Hello, what can the issue be when machine-001 has curing temperature reading of 179.2°C that exceeds warning threshold of 178°C?
                """,
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
