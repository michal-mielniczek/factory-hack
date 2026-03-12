using Azure.Identity;
using Azure.AI.Projects;
using System.Text.Json;

using Microsoft.Agents.AI;
using Microsoft.Agents.AI.Workflows;

using Azure.Monitor.OpenTelemetry.Exporter;

using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

using FactoryWorkflow;

// ============================================================================
// Application Startup
// ============================================================================

DotNetEnv.Env.TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);

// Configure services
builder.Services.AddHttpClient();
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});
builder.Configuration.AddEnvironmentVariables();

// Register Azure AI Project client for Agent Service access
builder.Services.AddSingleton(sp =>
{
    var endpoint = sp.GetRequiredService<IConfiguration>()["AZURE_AI_PROJECT_ENDPOINT"]
        ?? throw new InvalidOperationException("AZURE_AI_PROJECT_ENDPOINT is required");
    return new AIProjectClient(new Uri(endpoint), new DefaultAzureCredential());
});

builder.Services.AddSingleton<ILoggerFactory>(sp => LoggerFactory.Create(b => b.AddConsole()));

// Configure OpenTelemetry tracing
ConfigureTracing(builder);

var app = builder.Build();
app.UseDeveloperExceptionPage();
app.UseCors();

// ============================================================================
// API Endpoints
// ============================================================================

app.MapGet("/health", () => Results.Ok(new { Status = "Healthy", Timestamp = DateTimeOffset.UtcNow }));

app.MapPost("/api/analyze_machine", async (
    AnalyzeRequest request,
    AIProjectClient projectClient,
    IConfiguration config,
    ILoggerFactory loggerFactory,
    ILogger<Program> logger) =>
{
    logger.LogInformation("Starting analysis for machine {MachineId}", request.machine_id);

    try
    {
        // ================================================================
        // Step 1: Collect all agents for the workflow pipeline
        // ================================================================
        // The workflow executes agents sequentially, passing text output
        // from each agent to the next. Agent order matters!
        //
        // Pipeline: AnomalyClassification → FaultDiagnosis → RepairPlanner
        //           → MaintenanceScheduler → PartsOrdering
        // ================================================================

        var agents = new List<AIAgent>();

        // Agent Service agents (Azure AI Foundry hosted)
        agents.AddRange(AgentServiceProvider.GetAgents(projectClient, logger));

        // Local agent with Cosmos DB tools
        var repairPlanner = LocalAgentProvider.GetRepairPlannerAgent(config, loggerFactory, logger);
        if (repairPlanner != null) agents.Add(repairPlanner);

        // A2A agents (Python services from Challenge 3)
        agents.AddRange(await A2AAgentProvider.GetAgentsAsync(config, logger));

        logger.LogInformation("Workflow pipeline: [{Agents}]", string.Join(" → ", agents.Select(a => a.Name)));

        // ================================================================
        // Step 2: Build and execute the workflow
        // ================================================================
        // Uses WorkflowBuilder to create a sequential pipeline.
        // TextOnlyAgentExecutor strips MCP tool history between agents
        // to work around SDK deserialization issues.
        // ================================================================

        var telemetryJson = JsonSerializer.Serialize(request);
        var workflowResult = await ExecuteWorkflowAsync(agents, telemetryJson, logger);

        return Results.Ok(workflowResult);
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Workflow failed for machine {MachineId}", request.machine_id);
        return Results.Problem(
            detail: ex.ToString(),
            title: ex.Message,
            statusCode: 500);
    }
});

app.Run();

// ============================================================================
// Workflow Execution
// ============================================================================

static async Task<WorkflowResponse> ExecuteWorkflowAsync(
    List<AIAgent> agents,
    string input,
    ILogger logger)
{
    // Create executors that pass only text between agents
    var executors = agents.Select(a => new TextOnlyAgentExecutor(a)).ToList();

    // Clear results from any previous run
    TextOnlyAgentExecutor.ClearResults();

    // Build sequential workflow: agent1 → agent2 → agent3 → ...
    var workflowBuilder = new WorkflowBuilder(executors[0]);
    for (int i = 1; i < executors.Count; i++)
    {
        workflowBuilder.BindExecutor(executors[i]);
        workflowBuilder.AddEdge(executors[i - 1], executors[i]);
    }
    workflowBuilder.WithOutputFrom(executors[^1]);

    var workflow = workflowBuilder.Build();
    logger.LogInformation("Workflow built with {Count} agents", executors.Count);

    // Execute the workflow
    var run = await InProcessExecution.Default.RunAsync<string>(workflow, input);

    // Extract final output from workflow events
    string? finalOutput = null;
    foreach (var evt in run.OutgoingEvents)
    {
        if (evt is WorkflowOutputEvent outputEvent && outputEvent.Is<string>(out var text))
        {
            finalOutput = text;
        }
    }

    // Build response from collected step results
    return new WorkflowResponse
    {
        AgentSteps = TextOnlyAgentExecutor.StepResults.ToList(),
        FinalMessage = finalOutput ?? TextOnlyAgentExecutor.StepResults.LastOrDefault()?.FinalMessage
    };
}

// ============================================================================
// Telemetry Configuration
// ============================================================================

static void ConfigureTracing(WebApplicationBuilder builder)
{
    const string SourceName = "FactoryWorkflow";

    var resourceBuilder = ResourceBuilder.CreateDefault()
        .AddService(
            serviceName: builder.Environment.ApplicationName,
            serviceVersion: typeof(Program).Assembly.GetName().Version?.ToString())
        .AddAttributes([
            new KeyValuePair<string, object>("deployment.environment", builder.Environment.EnvironmentName),
        ]);

    var tracerBuilder = Sdk.CreateTracerProviderBuilder()
        .SetResourceBuilder(resourceBuilder)
        .AddSource(SourceName, "ChatClient")
        .AddSource("Microsoft.Agents.AI*")
        .AddSource("AnomalyClassificationAgent", "FaultDiagnosisAgent", "RepairPlannerAgent")
        .AddSource("MaintenanceSchedulerAgent", "PartsOrderingAgent")
        .AddAspNetCoreInstrumentation()
        .AddHttpClientInstrumentation()
        .AddOtlpExporter();

    var appInsightsConnectionString = builder.Configuration["ApplicationInsights:ConnectionString"];
    if (!string.IsNullOrWhiteSpace(appInsightsConnectionString))
    {
        tracerBuilder.AddAzureMonitorTraceExporter(options =>
            options.ConnectionString = appInsightsConnectionString);
    }

    tracerBuilder.Build();
}
