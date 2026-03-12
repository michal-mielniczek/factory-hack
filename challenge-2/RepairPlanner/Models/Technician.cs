using System.Text.Json.Serialization;
using Newtonsoft.Json;

namespace RepairPlanner.Models;

public sealed class Technician
{
    [JsonPropertyName("id")]
    [JsonProperty("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    [JsonProperty("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("department")]
    [JsonProperty("department")]
    public string Department { get; set; } = string.Empty;

    [JsonPropertyName("skills")]
    [JsonProperty("skills")]
    public List<string> Skills { get; set; } = [];

    [JsonPropertyName("certifications")]
    [JsonProperty("certifications")]
    public List<string> Certifications { get; set; } = [];

    [JsonPropertyName("available")]
    [JsonProperty("available")]
    public bool Available { get; set; }

    [JsonPropertyName("assignedWorkOrders")]
    [JsonProperty("assignedWorkOrders")]
    public List<string> AssignedWorkOrders { get; set; } = [];
}
