using api.Models;
using Microsoft.Azure.Cosmos;

namespace api.Services;

public class AnalysisService
{
    private readonly CosmosContainers _cosmos;

    public AnalysisService(CosmosContainers cosmos)
    {
        _cosmos = cosmos;
    }

    public async Task<Analysis> CreateAsync(Analysis analysis)
    {
        var response = await _cosmos.Analyses.CreateItemAsync(analysis, new PartitionKey(analysis.UserId));
        return response.Resource;
    }

    /// <summary>
    /// Deletes any existing analyses for the same (userId, month, bank) before saving a new one.
    /// "Same month overwrites" — but a Chase March + Amex March stay as two separate records.
    /// </summary>
    public async Task<int> DeleteByMonthAndBankAsync(string userId, string month, string bank)
    {
        if (string.IsNullOrWhiteSpace(month)) return 0;
        var bankNorm = (bank ?? string.Empty).Trim();

        var query = new QueryDefinition(
            "SELECT c.id, c.bank FROM c WHERE c.userId = @userId AND c.month = @month")
            .WithParameter("@userId", userId)
            .WithParameter("@month", month);

        var ids = new List<string>();
        using var iterator = _cosmos.Analyses.GetItemQueryIterator<Analysis>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(userId)
        });
        while (iterator.HasMoreResults)
        {
            var batch = await iterator.ReadNextAsync();
            foreach (var item in batch)
            {
                if (string.IsNullOrEmpty(item.Id)) continue;
                var existing = (item.Bank ?? string.Empty).Trim();
                if (string.Equals(existing, bankNorm, StringComparison.OrdinalIgnoreCase))
                    ids.Add(item.Id);
            }
        }

        foreach (var id in ids)
        {
            try
            {
                await _cosmos.Analyses.DeleteItemAsync<Analysis>(id, new PartitionKey(userId));
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // ignore
            }
        }
        return ids.Count;
    }

    public async Task<List<Analysis>> ListAsync(string userId)
    {
        var query = new QueryDefinition(
            "SELECT c.id, c.month, c.bank, c.analyzedAt, c.totalSpent FROM c WHERE c.userId = @userId ORDER BY c.analyzedAt DESC")
            .WithParameter("@userId", userId);

        var results = new List<Analysis>();
        using var iterator = _cosmos.Analyses.GetItemQueryIterator<Analysis>(query, requestOptions: new QueryRequestOptions
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

    public async Task<Analysis?> GetAsync(string userId, string id)
    {
        try
        {
            var response = await _cosmos.Analyses.ReadItemAsync<Analysis>(id, new PartitionKey(userId));
            return response.Resource;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return null;
        }
    }

    public async Task<bool> DeleteAsync(string userId, string id)
    {
        try
        {
            await _cosmos.Analyses.DeleteItemAsync<Analysis>(id, new PartitionKey(userId));
            return true;
        }
        catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
        {
            return false;
        }
    }

    public async Task<int> DeleteAllAsync(string userId)
    {
        var query = new QueryDefinition("SELECT c.id FROM c WHERE c.userId = @userId")
            .WithParameter("@userId", userId);

        var ids = new List<string>();
        using var iterator = _cosmos.Analyses.GetItemQueryIterator<Analysis>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(userId)
        });
        while (iterator.HasMoreResults)
        {
            var batch = await iterator.ReadNextAsync();
            foreach (var item in batch)
            {
                if (!string.IsNullOrEmpty(item.Id))
                    ids.Add(item.Id);
            }
        }

        foreach (var id in ids)
        {
            try
            {
                await _cosmos.Analyses.DeleteItemAsync<Analysis>(id, new PartitionKey(userId));
            }
            catch (CosmosException ex) when (ex.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                // ignore
            }
        }
        return ids.Count;
    }

    public async Task<List<LedgerEntry>> GetAllTransactionsAsync(string userId)
    {
        var query = new QueryDefinition("SELECT * FROM c WHERE c.userId = @userId")
            .WithParameter("@userId", userId);

        var entries = new List<LedgerEntry>();
        using var iterator = _cosmos.Analyses.GetItemQueryIterator<Analysis>(query, requestOptions: new QueryRequestOptions
        {
            PartitionKey = new PartitionKey(userId)
        });
        while (iterator.HasMoreResults)
        {
            var batch = await iterator.ReadNextAsync();
            foreach (var analysis in batch)
            {
                foreach (var cat in analysis.Categories)
                {
                    int i = 0;
                    foreach (var tx in cat.Transactions)
                    {
                        entries.Add(new LedgerEntry
                        {
                            Id = $"{analysis.Id}:{cat.Name}:{i++}",
                            AnalysisId = analysis.Id,
                            Month = analysis.Month,
                            Bank = analysis.Bank,
                            Date = tx.Date,
                            Category = cat.Name,
                            Merchant = tx.Merchant,
                            Description = tx.Description,
                            Amount = tx.Amount
                        });
                    }
                }
            }
        }
        return entries;
    }
}
