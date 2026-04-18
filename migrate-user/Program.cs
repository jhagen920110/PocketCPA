using System.Net;
using System.Text.Json;
using Microsoft.Azure.Cosmos;
using Newtonsoft.Json.Linq;

// Usage:
//   dotnet run -- <endpoint> <key> <database> --list
//   dotnet run -- <endpoint> <key> <database> <fromUserId> <toUserId> [--apply]
// Without --apply, migration runs in dry-run mode.

if (args.Length < 4)
{
    Console.WriteLine("Usage:");
    Console.WriteLine("  dotnet run -- <endpoint> <key> <database> --list");
    Console.WriteLine("  dotnet run -- <endpoint> <key> <database> <fromUserId> <toUserId> [--apply]");
    return 1;
}

var endpoint = args[0];
var key = args[1];
var database = args[2];
using var client = new CosmosClient(endpoint, key);
var db = client.GetDatabase(database);

if (args[3] == "--list")
{
    foreach (var containerName in new[] { "statements", "analyses" })
    {
        var container = db.GetContainer(containerName);
        Console.WriteLine($"\n=== {containerName} — userId counts ===");
        var counts = new Dictionary<string, int>();
        var query = new QueryDefinition("SELECT c.userId FROM c");
        using var it = container.GetItemQueryIterator<UserIdRow>(query);
        while (it.HasMoreResults)
        {
            var batch = await it.ReadNextAsync();
            foreach (var item in batch)
            {
                var uid = item.userId ?? "(null)";
                counts[uid] = counts.GetValueOrDefault(uid) + 1;
            }
        }
        foreach (var kv in counts.OrderByDescending(k => k.Value))
            Console.WriteLine($"  {kv.Key,-40} {kv.Value}");
    }
    return 0;
}

if (args.Length < 5)
{
    Console.WriteLine("Migration mode requires: <endpoint> <key> <database> <fromUserId> <toUserId> [--apply]");
    return 1;
}

var fromUserId = args[3];
var toUserId = args[4];
var apply = args.Contains("--apply");

foreach (var containerName in new[] { "statements", "analyses" })
{
    var container = db.GetContainer(containerName);
    Console.WriteLine($"\n=== {containerName} ===");

    var query = new QueryDefinition("SELECT * FROM c WHERE c.userId = @u").WithParameter("@u", fromUserId);
    using var iterator = container.GetItemQueryIterator<JObject>(query, requestOptions: new QueryRequestOptions
    {
        PartitionKey = new PartitionKey(fromUserId)
    });

    int scanned = 0, inserted = 0, deleted = 0, errors = 0;
    while (iterator.HasMoreResults)
    {
        var batch = await iterator.ReadNextAsync();
        foreach (var item in batch)
        {
            scanned++;
            var id = item["id"]?.Value<string>();
            if (string.IsNullOrEmpty(id)) continue;

            if (!apply)
            {
                var month = item["month"]?.ToString() ?? "";
                Console.WriteLine($"  [dry] would migrate {id} ({month})");
                continue;
            }

            // Strip Cosmos system props and set new userId.
            var clone = (JObject)item.DeepClone();
            clone["userId"] = toUserId;
            foreach (var sys in new[] { "_rid", "_etag", "_ts", "_self", "_attachments" })
                clone.Remove(sys);

            try
            {
                await container.UpsertItemAsync(clone, new PartitionKey(toUserId));
                inserted++;
            }
            catch (CosmosException ex)
            {
                Console.Error.WriteLine($"  upsert failed for {id}: {ex.Message}");
                errors++;
                continue;
            }

            try
            {
                await container.DeleteItemAsync<JObject>(id, new PartitionKey(fromUserId));
                deleted++;
            }
            catch (CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound) { }
            catch (CosmosException ex)
            {
                Console.Error.WriteLine($"  delete failed for {id}: {ex.Message}");
                errors++;
            }
        }
    }

    Console.WriteLine($"  scanned={scanned} inserted={inserted} deleted={deleted} errors={errors} apply={apply}");
}

Console.WriteLine("\nDone.");
return 0;

record UserIdRow(string? userId);
