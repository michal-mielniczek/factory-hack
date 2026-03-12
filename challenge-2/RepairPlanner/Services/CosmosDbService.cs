using System.Net;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
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

    public CosmosDbService(
        string endpoint,
        string key,
        string databaseName,
        ILogger<CosmosDbService>? logger = null)
    {
        _client = new CosmosClient(endpoint, key, new CosmosClientOptions
        {
            ApplicationName = "RepairPlanner",
        });
        _ownsClient = true;
        _logger = logger ?? NullLogger<CosmosDbService>.Instance;

        var database = _client.GetDatabase(databaseName);
        _techniciansContainer = database.GetContainer("Technicians");
        _partsContainer = database.GetContainer("PartsInventory");
        _workOrdersContainer = database.GetContainer("WorkOrders");
    }

    public CosmosDbService(
        CosmosClient cosmosClient,
        string databaseName,
        ILogger<CosmosDbService>? logger = null)
    {
        _client = cosmosClient;
        _ownsClient = false;
        _logger = logger ?? NullLogger<CosmosDbService>.Instance;

        var database = _client.GetDatabase(databaseName);
        _techniciansContainer = database.GetContainer("Technicians");
        _partsContainer = database.GetContainer("PartsInventory");
        _workOrdersContainer = database.GetContainer("WorkOrders");
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    public async Task<IReadOnlyList<Technician>> GetAvailableTechniciansWithSkillsAsync(
        IReadOnlyList<string> requiredSkills,
        string department = "Maintenance",
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

            queryText += requireAllSkills
                ? $" AND ({string.Join(" AND ", clauses)})"
                : $" AND ({string.Join(" OR ", clauses)})";
        }

        var queryDefinition = new QueryDefinition(queryText);
        foreach (var (name, value) in parameters)
        {
            queryDefinition = queryDefinition.WithParameter(name, value);
        }

        return await QueryItemsAsync<Technician>(
            _techniciansContainer,
            queryDefinition,
            string.IsNullOrWhiteSpace(department) ? null : new PartitionKey(department),
            cancellationToken);
    }

    public async Task<IReadOnlyList<Part>> GetPartsByPartNumbersAsync(
        IReadOnlyList<string> partNumbers,
        CancellationToken cancellationToken = default)
    {
        partNumbers ??= [];
        var normalizedPartNumbers = partNumbers
            .Where(partNumber => !string.IsNullOrWhiteSpace(partNumber))
            .Select(partNumber => partNumber.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (normalizedPartNumbers.Length == 0)
        {
            return [];
        }

        var queryDefinition = new QueryDefinition(
            "SELECT * FROM c WHERE ARRAY_CONTAINS(@partNumbers, c.partNumber)")
            .WithParameter("@partNumbers", normalizedPartNumbers);

        return await QueryItemsAsync<Part>(_partsContainer, queryDefinition, null, cancellationToken);
    }

    public async Task<string> CreateWorkOrderAsync(
        WorkOrder workOrder,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(workOrder);

        if (string.IsNullOrWhiteSpace(workOrder.Id))
        {
            workOrder.Id = $"wo-{DateTimeOffset.UtcNow:yyyy}-{Guid.NewGuid():N}"[..19];
        }

        var response = await _workOrdersContainer.CreateItemAsync(
            workOrder,
            new PartitionKey(workOrder.Status),
            cancellationToken: cancellationToken);

        _logger.LogInformation("Created work order {WorkOrderId}", response.Resource.Id);
        return response.Resource.Id;
    }

    private async Task<IReadOnlyList<T>> QueryItemsAsync<T>(
        Container container,
        QueryDefinition queryDefinition,
        PartitionKey? partitionKey,
        CancellationToken cancellationToken)
    {
        var requestOptions = new QueryRequestOptions
        {
            PartitionKey = partitionKey,
        };

        var results = new List<T>();

        try
        {
            using var iterator = container.GetItemQueryIterator<T>(
                queryDefinition,
                requestOptions: requestOptions);

            while (iterator.HasMoreResults)
            {
                var response = await iterator.ReadNextAsync(cancellationToken);
                results.AddRange(response);
            }
        }
        catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            _logger.LogWarning(ex, "Container or database not found during query.");
        }

        return results;
    }
}
