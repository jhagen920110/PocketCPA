using System.Net;
using api.Middleware;
using api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace api.Functions;

public class LedgerFunctions
{
    private readonly AnalysisService _analysisService;

    public LedgerFunctions(AnalysisService analysisService)
    {
        _analysisService = analysisService;
    }

    [Function("GetLedger")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "ledger")] HttpRequestData req,
        FunctionContext context)
    {
        var userId = context.GetUserId();
        if (userId == null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var entries = await _analysisService.GetAllTransactionsAsync(userId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(entries);
        return response;
    }
}
