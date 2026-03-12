namespace RepairPlanner.Services;

public sealed record CosmosDbOptions(
    string Endpoint,
    string Key,
    string DatabaseName,
    string TechniciansContainerName = "Technicians",
    string PartsContainerName = "PartsInventory",
    string WorkOrdersContainerName = "WorkOrders");
