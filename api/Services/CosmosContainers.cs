using api.Models;
using Microsoft.Azure.Cosmos;
using Microsoft.Extensions.Options;

namespace api.Services;

public class CosmosContainers
{
    private readonly CosmosClient _client;
    private readonly Database _database;

    public Container Statements { get; }
    public Container Analyses { get; }

    public CosmosContainers(CosmosClient client, IOptions<CosmosDbOptions> options)
    {
        _client = client;
        _database = _client.GetDatabase(options.Value.DatabaseName);
        Statements = _database.GetContainer("statements");
        Analyses = _database.GetContainer("analyses");
    }
}
