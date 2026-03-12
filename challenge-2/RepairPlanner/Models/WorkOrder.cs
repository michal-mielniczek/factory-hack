using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

public sealed class WorkOrder
{
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("workOrderNumber")]
    [JsonProperty("workOrderNumber")]
    public string WorkOrderNumber { get; set; } = string.Empty;

    [JsonPropertyName("machineId")]
    [JsonProperty("machineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("faultType")]
    [JsonProperty("faultType")]
    public string FaultType { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    [JsonProperty("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    [JsonProperty("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("type")]
    [JsonProperty("type")]
    public string Type { get; set; } = "corrective";

    [JsonPropertyName("priority")]
    [JsonProperty("priority")]
    public string Priority { get; set; } = "medium";

    [JsonPropertyName("status")]
    [JsonProperty("status")]
    public string Status { get; set; } = "scheduled";

    [JsonPropertyName("assignedTo")]
    [JsonProperty("assignedTo")]
    public string? AssignedTo { get; set; }

    [JsonPropertyName("notes")]
    [JsonProperty("notes")]
    public string? Notes { get; set; }

    [JsonPropertyName("estimatedDuration")]
    [JsonProperty("estimatedDuration")]
    public int EstimatedDuration { get; set; }

    [JsonPropertyName("createdDate")]
    [JsonProperty("createdDate")]
    public DateTimeOffset CreatedDate { get; set; }

    [JsonPropertyName("tasks")]
    [JsonProperty("tasks")]
    public List<RepairTask> Tasks { get; set; } = [];

    [JsonPropertyName("requiredParts")]
    [JsonProperty("requiredParts")]
    public List<RequiredPart> RequiredParts { get; set; } = [];

    [JsonPropertyName("partsUsed")]
    [JsonProperty("partsUsed")]
    public List<WorkOrderPartUsage> PartsUsed { get; set; } = [];
}

public sealed class RequiredPart
{
    [JsonPropertyName("partId")]
    [JsonProperty("partId")]
    public string PartId { get; set; } = string.Empty;

    [JsonPropertyName("partNumber")]
    [JsonProperty("partNumber")]
    public string PartNumber { get; set; } = string.Empty;

    [JsonPropertyName("partName")]
    [JsonProperty("partName")]
    public string PartName { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    [JsonProperty("quantity")]
    public int Quantity { get; set; }

    [JsonPropertyName("isAvailable")]
    [JsonProperty("isAvailable")]
    public bool IsAvailable { get; set; }
}

public sealed class WorkOrderPartUsage
{
    [JsonPropertyName("partId")]
    [JsonProperty("partId")]
    public string PartId { get; set; } = string.Empty;

    [JsonPropertyName("partNumber")]
    [JsonProperty("partNumber")]
    public string PartNumber { get; set; } = string.Empty;

    [JsonPropertyName("quantity")]
    [JsonProperty("quantity")]
    public int Quantity { get; set; }
}
