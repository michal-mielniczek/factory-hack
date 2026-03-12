using Azure.AI.Projects;
using Microsoft.Extensions.Logging.Abstractions;
using RepairPlanner.Models;
using RepairPlanner.Services;

namespace RepairPlanner.Tests;

public sealed class RepairPlannerAgentTests
{
    [Fact]
    public void SelectBestTechnician_PrefersHighestSkillOverlap()
    {
        var requiredSkills = new[] { "temperature_control", "instrumentation", "electrical_systems" };
        var technicians =
            new List<Technician>
            {
                new()
                {
                    Id = "tech-001",
                    Name = "Alex",
                    Available = true,
                    Skills = ["temperature_control"],
                    CurrentAssignments = ["WO-1", "WO-2"],
                },
                new()
                {
                    Id = "tech-002",
                    Name = "Sam",
                    Available = true,
                    Skills = ["temperature_control", "instrumentation", "electrical_systems"],
                    CurrentAssignments = ["WO-3"],
                },
            };

        var selected = RepairPlannerAgent.SelectBestTechnician(technicians, requiredSkills);

        Assert.NotNull(selected);
        Assert.Equal("tech-002", selected!.Id);
    }

    [Fact]
    public void SelectBestTechnician_BreaksTiesWithFewerAssignments()
    {
        var requiredSkills = new[] { "plc_troubleshooting" };
        var technicians =
            new List<Technician>
            {
                new()
                {
                    Id = "tech-001",
                    Name = "Alex",
                    Available = true,
                    Skills = ["plc_troubleshooting"],
                    CurrentAssignments = ["WO-1", "WO-2"],
                },
                new()
                {
                    Id = "tech-002",
                    Name = "Sam",
                    Available = true,
                    Skills = ["plc_troubleshooting"],
                    CurrentAssignments = ["WO-3"],
                },
            };

        var selected = RepairPlannerAgent.SelectBestTechnician(technicians, requiredSkills);

        Assert.NotNull(selected);
        Assert.Equal("tech-002", selected!.Id);
    }
}
