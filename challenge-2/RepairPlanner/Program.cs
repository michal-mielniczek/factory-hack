using System.Text.Json;
using Azure.AI.Projects;
using Azure.Identity;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using RepairPlanner;
using RepairPlanner.Models;
using RepairPlanner.Services;

var services = new ServiceCollection();

services.AddLogging(builder =>
{
    builder.AddSimpleConsole(options => options.SingleLine = true);
    builder.SetMinimumLevel(LogLevel.Information);
});

var projectEndpoint = GetRequiredEnvVar("AZURE_AI_PROJECT_ENDPOINT");
var modelDeploymentName = GetRequiredEnvVar("MODEL_DEPLOYMENT_NAME");
var cosmosEndpoint = GetRequiredEnvVar("COSMOS_ENDPOINT");
var cosmosKey = GetRequiredEnvVar("COSMOS_KEY");
var cosmosDatabaseName = GetRequiredEnvVar("COSMOS_DATABASE_NAME");

services.AddSingleton(new AIProjectClient(new Uri(projectEndpoint), new DefaultAzureCredential()));
services.AddSingleton(new CosmosDbOptions(cosmosEndpoint, cosmosKey, cosmosDatabaseName));
services.AddSingleton<CosmosDbService>();
services.AddSingleton<IFaultMappingService, FaultMappingService>();
services.AddSingleton(sp => new RepairPlannerAgent(
    sp.GetRequiredService<AIProjectClient>(),
    sp.GetRequiredService<CosmosDbService>(),
    sp.GetRequiredService<IFaultMappingService>(),
    modelDeploymentName,
    sp.GetRequiredService<ILogger<RepairPlannerAgent>>()));

using var provider = services.BuildServiceProvider();
var logger = provider.GetRequiredService<ILoggerFactory>().CreateLogger("RepairPlanner");
var agent = provider.GetRequiredService<RepairPlannerAgent>();

try
{
    var fault = await LoadFaultAsync(args);

    logger.LogInformation("Ensuring RepairPlannerAgent version exists in Foundry...");
    await agent.EnsureAgentVersionAsync();

    logger.LogInformation("Planning work order for machine {MachineId} and fault {FaultType}", fault.MachineId, fault.FaultType);
    var workOrder = await agent.PlanAndCreateWorkOrderAsync(fault);

    Console.WriteLine(JsonSerializer.Serialize(workOrder, RepairPlannerAgent.JsonOptions));
}
catch (Exception ex)
{
    logger.LogError(ex, "Repair planner failed");
    Environment.ExitCode = 1;
}

static async Task<DiagnosedFault> LoadFaultAsync(IReadOnlyList<string> args)
{
    if (args.Count == 0)
    {
        return new DiagnosedFault
        {
            MachineId = "machine-001",
            FaultType = "curing_temperature_excessive",
            RootCause = "Heating element drift",
            Severity = "High",
            DetectedAt = DateTimeOffset.UtcNow,
            Metadata = new Dictionary<string, object?>
            {
                ["metric"] = "curing_temperature",
                ["value"] = 179.2,
                ["threshold"] = 178,
            },
        };
    }

    var input = args[0];
    var content = File.Exists(input) ? await File.ReadAllTextAsync(input) : input;
    var fault = JsonSerializer.Deserialize<DiagnosedFault>(content, RepairPlannerAgent.JsonOptions);

    return fault ?? throw new InvalidOperationException("Could not deserialize diagnosed fault input.");
}

static string GetRequiredEnvVar(string name)
{
    return Environment.GetEnvironmentVariable(name)
        ?? throw new InvalidOperationException($"Environment variable '{name}' is required.");
}
