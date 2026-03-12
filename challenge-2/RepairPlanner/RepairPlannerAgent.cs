using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.Projects;
using Azure.AI.Projects.OpenAI;
using Microsoft.Agents.AI;
using RepairPlanner.Models;
using RepairPlanner.Services;

namespace RepairPlanner;

public sealed class RepairPlannerAgent(
    AIProjectClient projectClient,
    CosmosDbService cosmosDb,
    IFaultMappingService faultMapping,
    string modelDeploymentName,
    ILogger<RepairPlannerAgent> logger)
{
    public const string AgentName = "RepairPlannerAgent";

    private const string AgentInstructions = """
        You are a Repair Planner Agent for tire manufacturing equipment.
        Generate a repair plan with tasks, timeline, and resource allocation.
        Return the response as valid JSON matching the WorkOrder schema.

        Output JSON with these fields:
        - workOrderNumber, machineId, title, description
        - type: "corrective" | "preventive" | "emergency"
        - priority: "critical" | "high" | "medium" | "low"
        - status, assignedTo, notes
        - estimatedDuration: integer minutes
        - requiredParts: [{ partId, partNumber, partName, quantity, isAvailable }]
        - partsUsed: [{ partId, partNumber, partName, quantity, isAvailable }]
        - tasks: [{ sequence, title, description, estimatedDurationMinutes, requiredSkills, safetyNotes }]

        Rules:
        - Assign the most qualified available technician from the provided candidates.
        - Include only relevant parts and preserve inventory availability.
        - Tasks must be ordered, actionable, and safe for industrial maintenance.
        - Output JSON only.
        """;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
        WriteIndented = true,
    };

    public async Task EnsureAgentVersionAsync(CancellationToken ct = default)
    {
        var definition = new PromptAgentDefinition(model: modelDeploymentName)
        {
            Instructions = AgentInstructions,
        };

        await projectClient.Agents.CreateAgentVersionAsync(
            AgentName,
            new AgentVersionCreationOptions(definition),
            ct);
    }

    public async Task<WorkOrder> PlanAndCreateWorkOrderAsync(DiagnosedFault fault, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(fault);

        var requiredSkills = faultMapping.GetRequiredSkills(fault.FaultType);
        var requiredPartNumbers = faultMapping.GetRequiredParts(fault.FaultType);

        var technicians = await cosmosDb.GetAvailableTechniciansWithSkillsAsync(requiredSkills, cancellationToken: ct);
        var parts = await cosmosDb.GetPartsByPartNumbersAsync(requiredPartNumbers, cancellationToken: ct);
        var selectedTechnician = SelectBestTechnician(technicians, requiredSkills);

        var prompt = BuildPrompt(fault, requiredSkills, technicians, parts, selectedTechnician);
        var workOrder = await GenerateWorkOrderAsync(prompt, ct);
        NormalizeWorkOrder(workOrder, fault, selectedTechnician, parts);

        var createdId = await cosmosDb.CreateWorkOrderAsync(workOrder, ct);
        workOrder.Id = createdId;

        return workOrder;
    }

    public static Technician? SelectBestTechnician(
        IReadOnlyList<Technician> technicians,
        IReadOnlyList<string> requiredSkills)
    {
        if (technicians.Count == 0)
        {
            return null;
        }

        return technicians
            .OrderByDescending(technician => requiredSkills.Count(skill =>
                technician.Skills.Contains(skill, StringComparer.OrdinalIgnoreCase)))
            .ThenBy(technician => technician.CurrentAssignments.Count)
            .ThenBy(technician => technician.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private async Task<WorkOrder> GenerateWorkOrderAsync(string prompt, CancellationToken ct)
    {
        try
        {
            var agent = projectClient.GetAIAgent(name: AgentName);
            var response = await agent.RunAsync(prompt, thread: null, options: null, cancellationToken: ct);

            if (string.IsNullOrWhiteSpace(response.Text))
            {
                throw new InvalidOperationException("Agent returned an empty response.");
            }

            var workOrder = JsonSerializer.Deserialize<WorkOrder>(response.Text, JsonOptions);
            return workOrder ?? throw new InvalidOperationException("Agent response could not be parsed as WorkOrder JSON.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Falling back to deterministic work-order draft because agent invocation failed.");
            return new WorkOrder();
        }
    }

    private static string BuildPrompt(
        DiagnosedFault fault,
        IReadOnlyList<string> requiredSkills,
        IReadOnlyList<Technician> technicians,
        IReadOnlyList<Part> parts,
        Technician? selectedTechnician)
    {
        var builder = new StringBuilder();
        builder.AppendLine("Create a corrective maintenance work order from this diagnosed fault.");
        builder.AppendLine();
        builder.AppendLine("Diagnosed fault:");
        builder.AppendLine(JsonSerializer.Serialize(fault, JsonOptions));
        builder.AppendLine();
        builder.AppendLine("Required skills:");
        builder.AppendLine(JsonSerializer.Serialize(requiredSkills, JsonOptions));
        builder.AppendLine();
        builder.AppendLine("Candidate technicians:");
        builder.AppendLine(JsonSerializer.Serialize(technicians, JsonOptions));
        builder.AppendLine();
        builder.AppendLine("Recommended technician:");
        builder.AppendLine(JsonSerializer.Serialize(selectedTechnician, JsonOptions));
        builder.AppendLine();
        builder.AppendLine("Available parts:");
        builder.AppendLine(JsonSerializer.Serialize(parts, JsonOptions));
        builder.AppendLine();
        builder.AppendLine("Return JSON only.");

        return builder.ToString();
    }

    private static void NormalizeWorkOrder(
        WorkOrder workOrder,
        DiagnosedFault fault,
        Technician? selectedTechnician,
        IReadOnlyList<Part> parts)
    {
        workOrder.Id = string.IsNullOrWhiteSpace(workOrder.Id)
            ? $"wo-{DateTimeOffset.UtcNow:yyyy}-{Guid.NewGuid():N}"[..16]
            : workOrder.Id;
        workOrder.WorkOrderNumber ??= $"WO-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";
        workOrder.MachineId = string.IsNullOrWhiteSpace(workOrder.MachineId) ? fault.MachineId : workOrder.MachineId;
        workOrder.FaultType = string.IsNullOrWhiteSpace(workOrder.FaultType) ? fault.FaultType : workOrder.FaultType;
        workOrder.Title ??= $"Repair Plan - {fault.FaultType}";
        workOrder.Description ??= fault.RootCause ?? $"Repair plan for {fault.FaultType}";
        workOrder.Type = NormalizeType(workOrder.Type);
        workOrder.Priority = NormalizePriority(workOrder.Priority, fault.Severity);
        workOrder.Status ??= "Created";
        workOrder.AssignedTo ??= selectedTechnician?.Id;
        workOrder.AssignedTechnician ??= selectedTechnician;
        workOrder.CreatedDate ??= DateTimeOffset.UtcNow;
        workOrder.EstimatedDuration ??= workOrder.Tasks.Sum(task => task.EstimatedDurationMinutes);
        workOrder.Notes ??= "Generated by RepairPlannerAgent.";

        if (workOrder.Tasks.Count == 0)
        {
            workOrder.Tasks.AddRange(CreateFallbackTasks(fault));
        }

        var requiredParts = parts.Select(part => new WorkOrderPartUsage
        {
            PartId = part.Id,
            PartNumber = part.PartNumber,
            PartName = part.Name,
            Quantity = 1,
            IsAvailable = part.QuantityInStock > 0,
        }).ToList();

        if (workOrder.RequiredParts.Count == 0)
        {
            workOrder.RequiredParts = requiredParts;
        }

        if (workOrder.PartsUsed.Count == 0)
        {
            workOrder.PartsUsed = requiredParts;
        }
    }

    private static string NormalizeType(string? type)
    {
        return type?.ToLowerInvariant() switch
        {
            "preventive" => "preventive",
            "emergency" => "emergency",
            _ => "corrective",
        };
    }

    private static string NormalizePriority(string? priority, string? severity)
    {
        var value = (priority ?? severity ?? "medium").ToLowerInvariant();
        return value switch
        {
            "critical" => "critical",
            "high" => "high",
            "low" => "low",
            _ => "medium",
        };
    }

    private static IReadOnlyList<RepairTask> CreateFallbackTasks(DiagnosedFault fault)
    {
        return
        [
            new RepairTask
            {
                Sequence = 1,
                Title = "Lock out and verify machine isolation",
                Description = $"Safely isolate {fault.MachineId} before inspection.",
                EstimatedDurationMinutes = 20,
                RequiredSkills = ["lockout_tagout", "safety_compliance"],
                SafetyNotes = "Apply LOTO and verify zero-energy state.",
            },
            new RepairTask
            {
                Sequence = 2,
                Title = "Inspect and repair root cause",
                Description = $"Diagnose and repair {fault.FaultType}.",
                EstimatedDurationMinutes = 60,
                RequiredSkills = [fault.FaultType],
                SafetyNotes = "Use machine-specific PPE and follow maintenance manual.",
            },
            new RepairTask
            {
                Sequence = 3,
                Title = "Validate return to service",
                Description = "Test the machine and confirm telemetry is back within thresholds.",
                EstimatedDurationMinutes = 30,
                RequiredSkills = ["validation", "instrumentation"],
                SafetyNotes = "Keep operators clear until test cycle completes.",
            },
        ];
    }
}
