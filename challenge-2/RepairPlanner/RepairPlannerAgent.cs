using System.Text.Json;
using System.Text.Json.Serialization;
using Azure.AI.Projects;
using Microsoft.Extensions.Logging;
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
    private const string AgentName = "RepairPlannerAgent";
    private const string AgentInstructions = """
        You are a Repair Planner Agent for tire manufacturing equipment.
        Generate repair plans as valid JSON matching the workshop WorkOrder schema.
        Keep durations as integer minutes and keep output grounded to the provided technician and parts context.
        """;

    public static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        NumberHandling = JsonNumberHandling.AllowReadingFromString,
    };

    public async Task EnsureAgentVersionAsync(CancellationToken cancellationToken = default)
    {
        var definition = new PromptAgentDefinition(model: modelDeploymentName)
        {
            Instructions = AgentInstructions,
        };

        await projectClient.Agents.CreateAgentVersionAsync(
            AgentName,
            new AgentVersionCreationOptions(definition),
            cancellationToken);
    }

    public async Task<WorkOrder> PlanAndCreateWorkOrderAsync(
        DiagnosedFault fault,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fault);

        var requiredSkills = faultMapping.GetRequiredSkills(fault.FaultType);
        var requiredPartNumbers = faultMapping.GetRequiredParts(fault.FaultType);

        var technicians = await cosmosDb.GetAvailableTechniciansWithSkillsAsync(
            requiredSkills,
            department: "Maintenance",
            requireAllSkills: false,
            cancellationToken: cancellationToken);

        var parts = await cosmosDb.GetPartsByPartNumbersAsync(
            requiredPartNumbers,
            cancellationToken: cancellationToken);

        var selectedTechnician = SelectBestTechnician(technicians, requiredSkills);
        var workOrder = BuildDeterministicWorkOrder(fault, selectedTechnician, parts, requiredSkills);

        await cosmosDb.CreateWorkOrderAsync(workOrder, cancellationToken);

        logger.LogInformation(
            "Created work order {WorkOrderId} for machine {MachineId} and fault {FaultType}",
            workOrder.Id,
            workOrder.MachineId,
            workOrder.FaultType);

        return workOrder;
    }

    public static Technician? SelectBestTechnician(
        IReadOnlyList<Technician> technicians,
        IReadOnlyList<string> requiredSkills)
    {
        return technicians
            .OrderByDescending(technician => requiredSkills.Count(skill =>
                technician.Skills.Contains(skill, StringComparer.OrdinalIgnoreCase)))
            .ThenBy(technician => technician.AssignedWorkOrders.Count)
            .ThenBy(technician => technician.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault();
    }

    private static WorkOrder BuildDeterministicWorkOrder(
        DiagnosedFault fault,
        Technician? selectedTechnician,
        IReadOnlyList<Part> parts,
        IReadOnlyList<string> requiredSkills)
    {
        var normalizedPriority = NormalizePriority(fault.Severity);
        var requiredParts = parts
            .Select(part => new RequiredPart
            {
                PartId = part.Id,
                PartNumber = part.PartNumber,
                PartName = part.Name,
                Quantity = 1,
                IsAvailable = part.QuantityInStock > 0,
            })
            .ToList();

        var repairTasks = BuildTasks(fault, requiredSkills, requiredParts);
        var estimatedDuration = repairTasks.Sum(task => task.EstimatedDurationMinutes);

        return new WorkOrder
        {
            Id = $"wo-{DateTimeOffset.UtcNow:yyyy}-{Guid.NewGuid():N}"[..19],
            WorkOrderNumber = $"WO-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}",
            MachineId = fault.MachineId,
            FaultType = fault.FaultType,
            Title = $"Repair {fault.FaultType.Replace('_', ' ')} on {fault.MachineId}",
            Description = fault.RootCause ?? "Investigate and repair diagnosed fault.",
            Type = normalizedPriority is "critical" ? "emergency" : "corrective",
            Priority = normalizedPriority,
            Status = "scheduled",
            AssignedTo = selectedTechnician?.Id,
            Notes = BuildNotes(fault, selectedTechnician, requiredParts),
            EstimatedDuration = estimatedDuration,
            CreatedDate = DateTimeOffset.UtcNow,
            Tasks = repairTasks,
            RequiredParts = requiredParts,
            PartsUsed = requiredParts
                .Where(part => part.IsAvailable)
                .Select(part => new WorkOrderPartUsage
                {
                    PartId = part.PartId,
                    PartNumber = part.PartNumber,
                    Quantity = part.Quantity,
                })
                .ToList(),
        };
    }

    private static List<RepairTask> BuildTasks(
        DiagnosedFault fault,
        IReadOnlyList<string> requiredSkills,
        IReadOnlyList<RequiredPart> requiredParts)
    {
        return
        [
            new RepairTask
            {
                Sequence = 1,
                Title = "Isolate machine and confirm fault",
                Description = $"Validate {fault.FaultType} on {fault.MachineId} using current telemetry and recent maintenance history.",
                EstimatedDurationMinutes = 20,
                RequiredSkills = requiredSkills.ToList(),
                SafetyNotes = "Follow lockout/tagout before any physical inspection.",
            },
            new RepairTask
            {
                Sequence = 2,
                Title = "Inspect affected components",
                Description = requiredParts.Count == 0
                    ? "Inspect the faulted subsystem and identify worn or drifting components."
                    : $"Inspect the subsystem and prepare the required parts: {string.Join(", ", requiredParts.Select(part => part.PartNumber))}.",
                EstimatedDurationMinutes = 30,
                RequiredSkills = requiredSkills.ToList(),
                SafetyNotes = "Use calibrated tools and verify thermal surfaces are safe to access.",
            },
            new RepairTask
            {
                Sequence = 3,
                Title = "Repair, validate, and return to service",
                Description = "Complete the repair, validate machine output against thresholds, and document the result in the work order.",
                EstimatedDurationMinutes = 40,
                RequiredSkills = requiredSkills.ToList(),
                SafetyNotes = "Record post-repair measurements before production restart.",
            },
        ];
    }

    private static string BuildNotes(
        DiagnosedFault fault,
        Technician? selectedTechnician,
        IReadOnlyList<RequiredPart> requiredParts)
    {
        var technicianNote = selectedTechnician is null
            ? "No available technician matched the required skill profile."
            : $"Assigned technician: {selectedTechnician.Name} ({selectedTechnician.Id}).";

        var partsNote = requiredParts.Count == 0
            ? "No replacement parts mapped for this fault."
            : $"Required parts: {string.Join(", ", requiredParts.Select(part => $"{part.PartNumber}={(part.IsAvailable ? "in stock" : "order required")}"))}.";

        return $"{technicianNote} {partsNote}";
    }

    private static string NormalizePriority(string? severity) =>
        severity?.Trim().ToLowerInvariant() switch
        {
            "critical" => "critical",
            "high" => "high",
            "medium" => "medium",
            _ => "low",
        };
}
