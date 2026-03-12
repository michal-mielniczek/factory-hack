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
    public string? WorkOrderNumber { get; set; }

    [JsonPropertyName("machineId")]
    [JsonProperty("machineId")]
    public string MachineId { get; set; } = string.Empty;

    [JsonPropertyName("faultType")]
    [JsonProperty("faultType")]
    public string? FaultType { get; set; }

    [JsonPropertyName("title")]
    [JsonProperty("title")]
    public string? Title { get; set; }

    [JsonPropertyName("description")]
    [JsonProperty("description")]
    public string? Description { get; set; }

    [JsonPropertyName("type")]
    [JsonProperty("type")]
    public string? Type { get; set; }

    [JsonPropertyName("priority")]
    [JsonProperty("priority")]
    public string? Priority { get; set; }

    [JsonPropertyName("status")]
    [JsonProperty("status")]
    public string? Status { get; set; }

    [JsonPropertyName("assignedTo")]
    [JsonProperty("assignedTo")]
    public string? AssignedTo { get; set; }

    [JsonPropertyName("assignedTechnician")]
    [JsonProperty("assignedTechnician")]
    public Technician? AssignedTechnician { get; set; }

    [JsonPropertyName("createdDate")]
    [JsonProperty("createdDate")]
    public DateTimeOffset? CreatedDate { get; set; }

    [JsonPropertyName("estimatedDuration")]
    [JsonProperty("estimatedDuration")]
    public int? EstimatedDuration { get; set; }

    [JsonPropertyName("requiredParts")]
    [JsonProperty("requiredParts")]
    public List<WorkOrderPartUsage> RequiredParts { get; set; } = [];

    [JsonPropertyName("partsUsed")]
    [JsonProperty("partsUsed")]
    public List<WorkOrderPartUsage> PartsUsed { get; set; } = [];

    [JsonPropertyName("tasks")]
    [JsonProperty("tasks")]
    public List<RepairTask> Tasks { get; set; } = [];

    [JsonPropertyName("notes")]
    [JsonProperty("notes")]
    public string? Notes { get; set; }
}
