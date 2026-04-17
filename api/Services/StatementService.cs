using api.Models;
using Microsoft.Azure.Cosmos;

namespace api.Services;

public class StatementService
{
    private readonly CosmosContainers _cosmos;

    public StatementService(CosmosContainers cosmos)
    {
        _cosmos = cosmos;
    }

    public async Task<Statement> CreateAsync(Statement statement)
    {
        var response = await _cosmos.Statements.CreateItemAsync(statement, new PartitionKey(statement.UserId));
        return response.Resource;
    }

    public async Task<List<Statement>> ListAsync(string userId)
    {
        var query = new QueryDefinition(
            "SELECT c.id, c.fileName, c.month, c.uploadedAt FROM c WHERE c.userId = @userId ORDER BY c.uploadedAt DESC")
            .WithParameter("@userId", userId);

        var results = new List<Statement>();
        using var iterator = _cosmos.Statements.GetItemQueryIterator<Statement>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(userId)
        });

        while (iterator.HasMoreResults)
        {
            var batch = await iterator.ReadNextAsync();
            results.AddRange(batch);
        }

        return results;
    }

    public async Task<List<Statement>> GetByIdsAsync(string userId, List<string> ids)
    {
        var idParams = ids.Select((id, i) => ($"@id{i}", id)).ToList();
        var inClause = string.Join(",", idParams.Select(p => p.Item1));

        var queryDef = new QueryDefinition(
            $"SELECT c.id, c.fileName, c.rawContent, c.month FROM c WHERE c.userId = @userId AND c.id IN ({inClause})")
            .WithParameter("@userId", userId);

        foreach (var (name, value) in idParams)
            queryDef.WithParameter(name, value);

        var results = new List<Statement>();
        using var iterator = _cosmos.Statements.GetItemQueryIterator<Statement>(queryDef, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(userId)
        });

        while (iterator.HasMoreResults)
        {
            var batch = await iterator.ReadNextAsync();
            results.AddRange(batch);
        }

        return results;
    }

    public async Task DeleteAsync(string userId, string id)
    {
        await _cosmos.Statements.DeleteItemAsync<Statement>(id, new PartitionKey(userId));
    }
}
