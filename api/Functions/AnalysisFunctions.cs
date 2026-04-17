using System.Net;
using System.Text.Json;
using api.Middleware;
using api.Models;
using api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace api.Functions;

public class AnalysisFunctions
{
    private readonly StatementService _statementService;
    private readonly AnalysisService _analysisService;
    private readonly SpendingAnalyzerService _analyzerService;

    public AnalysisFunctions(
        StatementService statementService,
        AnalysisService analysisService,
        SpendingAnalyzerService analyzerService)
    {
        _statementService = statementService;
        _analysisService = analysisService;
        _analyzerService = analyzerService;
    }

    [Function("AnalyzeStatements")]
    public async Task<HttpResponseData> Analyze(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "analyze")] HttpRequestData req,
        FunctionContext context)
    {
        var userId = context.GetUserId();
        if (userId == null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var body = await JsonSerializer.DeserializeAsync<AnalyzeRequest>(req.Body);
        if (body == null || body.StatementIds.Count == 0)
        {
            var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
            await badReq.WriteAsJsonAsync(new { error = "statementIds are required" });
            return badReq;
        }

        var statements = await _statementService.GetByIdsAsync(userId, body.StatementIds);
        if (statements.Count == 0)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "No statements found" });
            return notFound;
        }

        Analysis result;
        try
        {
            result = await _analyzerService.AnalyzeAsync(statements, body.Month);
        }
        catch (MonthNotDeterminedException)
        {
            var needMonth = req.CreateResponse(HttpStatusCode.UnprocessableEntity);
            await needMonth.WriteAsJsonAsync(new
            {
                error = "AI could not determine the statement month. Please provide it.",
                needsMonth = true
            });
            return needMonth;
        }

        var analysis = new Analysis
        {
            UserId = userId,
            Month = result.Month,
            Bank = result.Bank,
            StatementIds = body.StatementIds,
            TotalSpent = result.TotalSpent,
            Categories = result.Categories,
            Insights = result.Insights,
            Suggestions = result.Suggestions,
            FunStats = result.FunStats
        };

        // Overwrite any existing analysis for the same (month, bank).
        await _analysisService.DeleteByMonthAndBankAsync(userId, analysis.Month, analysis.Bank);

        await _analysisService.CreateAsync(analysis);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(analysis);
        return response;
    }

    [Function("ListAnalyses")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analyses")] HttpRequestData req,
        FunctionContext context)
    {
        var userId = context.GetUserId();
        if (userId == null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var analyses = await _analysisService.ListAsync(userId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(analyses);
        return response;
    }

    [Function("GetAnalysis")]
    public async Task<HttpResponseData> Get(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "analyses/{id}")] HttpRequestData req,
        string id,
        FunctionContext context)
    {
        var userId = context.GetUserId();
        if (userId == null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var analysis = await _analysisService.GetAsync(userId, id);
        if (analysis == null)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Analysis not found" });
            return notFound;
        }

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(analysis);
        return response;
    }

    [Function("DeleteAnalysis")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "analyses/{id}")] HttpRequestData req,
        string id,
        FunctionContext context)
    {
        var userId = context.GetUserId();
        if (userId == null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var deleted = await _analysisService.DeleteAsync(userId, id);
        if (!deleted)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Analysis not found" });
            return notFound;
        }
        return req.CreateResponse(HttpStatusCode.NoContent);
    }

    [Function("DeleteAllAnalyses")]
    public async Task<HttpResponseData> DeleteAll(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "analyses")] HttpRequestData req,
        FunctionContext context)
    {
        var userId = context.GetUserId();
        if (userId == null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var count = await _analysisService.DeleteAllAsync(userId);
        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(new { deleted = count });
        return response;
    }
}
