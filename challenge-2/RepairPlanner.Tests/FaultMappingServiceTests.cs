using RepairPlanner.Services;

namespace RepairPlanner.Tests;

public sealed class FaultMappingServiceTests
{
    private readonly FaultMappingService _service = new();

    [Fact]
    public void GetRequiredSkills_ReturnsCanonicalSkills()
    {
        var skills = _service.GetRequiredSkills("curing_temperature_excessive");

        Assert.Contains("tire_curing_press", skills);
        Assert.Contains("temperature_control", skills);
        Assert.Contains("instrumentation", skills);
    }

    [Fact]
    public void GetRequiredParts_ReturnsEmptyForUnknownFault()
    {
        var parts = _service.GetRequiredParts("unknown_fault");

        Assert.Empty(parts);
    }

    [Fact]
    public void GetRequiredSkills_ReturnsFallbackForUnknownFault()
    {
        var skills = _service.GetRequiredSkills("unknown_fault");

        Assert.Equal(["general_maintenance"], skills);
    }
}
