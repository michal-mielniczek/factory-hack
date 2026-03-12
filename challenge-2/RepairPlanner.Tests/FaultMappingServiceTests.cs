using RepairPlanner.Services;
using Xunit;

namespace RepairPlanner.Tests;

public sealed class FaultMappingServiceTests
{
    private readonly FaultMappingService _service = new();

    [Fact]
    public void ReturnsExpectedSkillsForKnownFault()
    {
        var skills = _service.GetRequiredSkills("curing_temperature_excessive");

        Assert.Contains("temperature_control", skills);
        Assert.Contains("plc_troubleshooting", skills);
    }

    [Fact]
    public void ReturnsFallbackSkillsForUnknownFault()
    {
        var skills = _service.GetRequiredSkills("unknown_fault");

        Assert.Contains("general_maintenance", skills);
        Assert.Contains("safety_procedures", skills);
    }

    [Fact]
    public void ReturnsExpectedPartsForKnownFault()
    {
        var parts = _service.GetRequiredParts("mixing_temperature_excessive");

        Assert.Equal(["BMX-TIP-500", "GEN-TS-K400"], parts);
    }
}
