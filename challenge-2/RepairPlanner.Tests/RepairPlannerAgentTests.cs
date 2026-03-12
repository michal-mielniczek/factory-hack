using RepairPlanner;
using RepairPlanner.Models;
using Xunit;

namespace RepairPlanner.Tests;

public sealed class RepairPlannerAgentTests
{
    [Fact]
    public void SelectBestTechnician_PrefersHighestSkillCoverage()
    {
        var technicians = new List<Technician>
        {
            new()
            {
                Id = "tech-001",
                Name = "Alex",
                Skills = ["general_maintenance"],
                AssignedWorkOrders = ["wo-1"],
            },
            new()
            {
                Id = "tech-002",
                Name = "Jordan",
                Skills = ["temperature_control", "instrumentation", "plc_troubleshooting"],
                AssignedWorkOrders = [],
            },
        };

        var selected = RepairPlannerAgent.SelectBestTechnician(
            technicians,
            ["temperature_control", "plc_troubleshooting"]);

        Assert.NotNull(selected);
        Assert.Equal("tech-002", selected!.Id);
    }

    [Fact]
    public void SelectBestTechnician_UsesWorkloadAsTieBreaker()
    {
        var technicians = new List<Technician>
        {
            new()
            {
                Id = "tech-001",
                Name = "Avery",
                Skills = ["alignment", "bearing_replacement"],
                AssignedWorkOrders = ["wo-1", "wo-2"],
            },
            new()
            {
                Id = "tech-002",
                Name = "Blake",
                Skills = ["alignment", "bearing_replacement"],
                AssignedWorkOrders = [],
            },
        };

        var selected = RepairPlannerAgent.SelectBestTechnician(
            technicians,
            ["alignment", "bearing_replacement"]);

        Assert.NotNull(selected);
        Assert.Equal("tech-002", selected!.Id);
    }
}
