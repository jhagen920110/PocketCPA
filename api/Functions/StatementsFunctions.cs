using System.Net;
using System.Text.Json;
using api.Middleware;
using api.Models;
using api.Services;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace api.Functions;

public class StatementsFunctions
{
    private readonly StatementService _statementService;

    public StatementsFunctions(StatementService statementService)
    {
        _statementService = statementService;
    }

    [Function("UploadStatement")]
    public async Task<HttpResponseData> Upload(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "statements")] HttpRequestData req,
        FunctionContext context)
    {
        var userId = context.GetUserId();
        if (userId == null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var body = await JsonSerializer.DeserializeAsync<UploadRequest>(req.Body);
        if (body == null || string.IsNullOrEmpty(body.FileName) || string.IsNullOrEmpty(body.Content))
        {
            var badReq = req.CreateResponse(HttpStatusCode.BadRequest);
            await badReq.WriteAsJsonAsync(new { error = "fileName and content are required" });
            return badReq;
        }

        var statement = new Statement
        {
            UserId = userId,
            FileName = body.FileName,
            RawContent = body.Content,
            Month = body.Month
        };

        await _statementService.CreateAsync(statement);

        var response = req.CreateResponse(HttpStatusCode.Created);
        await response.WriteAsJsonAsync(new
        {
            id = statement.Id,
            fileName = statement.FileName,
            month = statement.Month,
            uploadedAt = statement.UploadedAt
        });
        return response;
    }

    [Function("ListStatements")]
    public async Task<HttpResponseData> List(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "statements")] HttpRequestData req,
        FunctionContext context)
    {
        var userId = context.GetUserId();
        if (userId == null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        var statements = await _statementService.ListAsync(userId);

        var response = req.CreateResponse(HttpStatusCode.OK);
        await response.WriteAsJsonAsync(statements);
        return response;
    }

    [Function("DeleteStatement")]
    public async Task<HttpResponseData> Delete(
        [HttpTrigger(AuthorizationLevel.Anonymous, "delete", Route = "statements/{id}")] HttpRequestData req,
        string id,
        FunctionContext context)
    {
        var userId = context.GetUserId();
        if (userId == null)
            return req.CreateResponse(HttpStatusCode.Unauthorized);

        try
        {
            await _statementService.DeleteAsync(userId, id);
            return req.CreateResponse(HttpStatusCode.NoContent);
        }
        catch (Microsoft.Azure.Cosmos.CosmosException ex) when (ex.StatusCode == HttpStatusCode.NotFound)
        {
            var notFound = req.CreateResponse(HttpStatusCode.NotFound);
            await notFound.WriteAsJsonAsync(new { error = "Statement not found" });
            return notFound;
        }
    }
}
