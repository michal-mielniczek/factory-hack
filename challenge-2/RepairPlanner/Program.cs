using System.Text.Json;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using RepairPlanner;
using RepairPlanner.Models;
using RepairPlanner.Services;

var loggerFactory = LoggerFactory.Create(builder => builder.AddSimpleConsole(options =>
{
    options.SingleLine = true;
    options.TimestampFormat = "HH:mm:ss ";
}));

var aiProjectEndpoint = GetRequiredEnvVar("AZURE_AI_PROJECT_ENDPOINT");
var modelDeploymentName = GetRequiredEnvVar("MODEL_DEPLOYMENT_NAME");
var cosmosEndpoint = GetRequiredEnvVar("COSMOS_ENDPOINT");
var cosmosKey = GetRequiredEnvVar("COSMOS_KEY");
var cosmosDatabaseName = GetRequiredEnvVar("COSMOS_DATABASE_NAME");

var projectClient = new AIProjectClient(new Uri(aiProjectEndpoint), new DefaultAzureCredential());
using var cosmosDbService = new CosmosDbService(
    cosmosEndpoint,
    cosmosKey,
    cosmosDatabaseName,
    loggerFactory.CreateLogger<CosmosDbService>());

var agent = new RepairPlannerAgent(
    projectClient,
    cosmosDbService,
    new FaultMappingService(),
    modelDeploymentName,
    loggerFactory.CreateLogger<RepairPlannerAgent>());

var diagnosedFault = await LoadDiagnosedFaultAsync(args);
await agent.EnsureAgentVersionAsync();
var workOrder = await agent.PlanAndCreateWorkOrderAsync(diagnosedFault);

Console.WriteLine(JsonSerializer.Serialize(workOrder, new JsonSerializerOptions
{
    WriteIndented = true,
}));

static async Task<DiagnosedFault> LoadDiagnosedFaultAsync(string[] args)
{
    if (args.Length == 0)
    {
        return new DiagnosedFault
        {
            MachineId = "machine-001",
            FaultType = "curing_temperature_excessive",
            RootCause = "Thermocouple drift causing heater overrun.",
            Severity = "High",
            DetectedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, object?>
            {
                ["metric"] = "curing_temperature",
                ["observedValue"] = 179.2,
                ["warningThreshold"] = 178.0,
            },
        };
    }

    var source = args[0];
    var payload = File.Exists(source) ? await File.ReadAllTextAsync(source) : source;
    return JsonSerializer.Deserialize<DiagnosedFault>(payload, RepairPlannerAgent.JsonOptions)
        ?? throw new InvalidOperationException("Could not deserialize diagnosed fault payload.");
}

static string GetRequiredEnvVar(string name) =>
    Environment.GetEnvironmentVariable(name)
    ?? throw new InvalidOperationException($"{name} environment variable is required.");
