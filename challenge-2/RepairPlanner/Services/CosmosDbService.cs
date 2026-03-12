using System.Net;
using Microsoft.Azure.Cosmos;
using RepairPlanner.Models;

namespace RepairPlanner.Services;

public sealed class CosmosDbService : IDisposable
{
    private readonly CosmosClient _client;
    private readonly bool _ownsClient;
    private readonly ILogger<CosmosDbService> _logger;
    private readonly Container _techniciansContainer;
    private readonly Container _partsContainer;
    private readonly Container _workOrdersContainer;

    public CosmosDbService(CosmosDbOptions options, ILogger<CosmosDbService> logger)
    {
        ArgumentNullException.ThrowIfNull(options);
        _logger = logger;

        _client = new CosmosClient(options.Endpoint, options.Key, new CosmosClientOptions
        {
            ApplicationName = "Challenge2RepairPlanner",
        });
        _ownsClient = true;

        var database = _client.GetDatabase(options.DatabaseName);
        _techniciansContainer = database.GetContainer(options.TechniciansContainerName);
        _partsContainer = database.GetContainer(options.PartsContainerName);
        _workOrdersContainer = database.GetContainer(options.WorkOrdersContainerName);
    }

    public CosmosDbService(
        CosmosClient client,
        string databaseName,
        ILogger<CosmosDbService> logger,
        string techniciansContainerName = "Technicians",
        string partsContainerName = "PartsInventory",
        string workOrdersContainerName = "WorkOrders")
    {
        _client = client;
        _logger = logger;
        _ownsClient = false;

        var database = _client.GetDatabase(databaseName);
        _techniciansContainer = database.GetContainer(techniciansContainerName);
        _partsContainer = database.GetContainer(partsContainerName);
        _workOrdersContainer = database.GetContainer(workOrdersContainerName);
    }

    public async Task<IReadOnlyList<Technician>> GetAvailableTechniciansWithSkillsAsync(
        IReadOnlyList<string> requiredSkills,
        string? department = "Maintenance",
        bool requireAllSkills = false,
        CancellationToken cancellationToken = default)
    {
        requiredSkills ??= [];

        var normalizedSkills = requiredSkills
            .Where(skill => !string.IsNullOrWhiteSpace(skill))
            .Select(skill => skill.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        var queryText = "SELECT * FROM c WHERE c.available = true";
        var parameters = new List<(string Name, object Value)>();

        if (!string.IsNullOrWhiteSpace(department))
        {
            queryText += " AND c.department = @department";
            parameters.Add(("@department", department));
        }

        if (normalizedSkills.Length > 0)
        {
            var clauses = new List<string>(normalizedSkills.Length);
            for (var index = 0; index < normalizedSkills.Length; index++)
            {
                var parameterName = $"@skill{index}";
                clauses.Add($"ARRAY_CONTAINS(c.skills, {parameterName})");
                parameters.Add((parameterName, normalizedSkills[index]));
            }

            queryText += $" AND ({string.Join(requireAllSkills ? " AND " : " OR ", clauses)})";
        }

        var queryDefinition = new QueryDefinition(queryText);
        foreach (var (name, value) in parameters)
        {
            queryDefinition = queryDefinition.WithParameter(name, value);
        }

        return await QueryItemsAsync(_techniciansContainer, queryDefinition, department is null ? null : new PartitionKey(department), cancellationToken);
    }

    public async Task<IReadOnlyList<Part>> GetPartsByPartNumbersAsync(
        IReadOnlyList<string> partNumbers,
        CancellationToken cancellationToken = default)
    {
        partNumbers ??= [];
        var normalizedPartNumbers = partNumbers
            .Where(part => !string.IsNullOrWhiteSpace(part))
            .Select(part => part.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedPartNumbers.Length == 0)
        {
            return [];
        }

        var queryDefinition = new QueryDefinition(
            "SELECT * FROM c WHERE ARRAY_CONTAINS(@partNumbers, c.partNumber)")
            .WithParameter("@partNumbers", normalizedPartNumbers);

        return await QueryItemsAsync(_partsContainer, queryDefinition, partitionKey: null, cancellationToken);
    }

    public async Task<string> CreateWorkOrderAsync(WorkOrder workOrder, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workOrder);

        workOrder.CreatedDate ??= DateTimeOffset.UtcNow;
        workOrder.Status ??= "Created";
        workOrder.WorkOrderNumber ??= $"WO-{DateTimeOffset.UtcNow:yyyyMMdd-HHmmss}";

        var response = await _workOrdersContainer.CreateItemAsync(
            workOrder,
            new PartitionKey(workOrder.Status),
            cancellationToken: cancellationToken);

        _logger.LogInformation(
            "Created work order {WorkOrderNumber} for machine {MachineId}",
            response.Resource.WorkOrderNumber,
            response.Resource.MachineId);

        return response.Resource.Id;
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    private async Task<IReadOnlyList<T>> QueryItemsAsync<T>(
        Container container,
        QueryDefinition queryDefinition,
        PartitionKey? partitionKey,
        CancellationToken cancellationToken)
    {
        try
        {
            var results = new List<T>();
            using var iterator = container.GetItemQueryIterator<T>(
                queryDefinition,
                requestOptions: new QueryRequestOptions { PartitionKey = partitionKey });

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(response);
            }

            return results;
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning(ex, "Cosmos container not found while querying {ContainerId}", container.Id);
            return [];
        }
    }
}
