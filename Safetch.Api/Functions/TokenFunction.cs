using System;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Hosting;
using Safetch.Core.Auth;

namespace Safetch.Api.Functions;

public class TokenFunction
{
    private readonly IApiKeyStore _store;
    private readonly bool _isDevelopment;

    // Fake identity used when running locally in Development mode
    private static readonly EasyAuthIdentity DevIdentity = new("dev-user", "local-dev");

    public TokenFunction(IApiKeyStore store, IHostEnvironment environment)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _isDevelopment = environment?.IsDevelopment() ?? false;
    }

    /// <summary>
    /// GET /api/token — returns existing API key for the authenticated user, or 404.
    /// </summary>
    [Function("TokenGet")]
    public async Task<HttpResponseData> GetToken(
        [HttpTrigger(AuthorizationLevel.Anonymous, "get", Route = "token")] HttpRequestData req,
        FunctionContext executionContext)
    {
        var identity = GetIdentity(req);
        if (identity == null)
            return await ErrorAsync(req, HttpStatusCode.Unauthorized, "Authentication required.", "UNAUTHENTICATED");

        var key = await _store.GetKeyAsync(identity.UserId, executionContext.CancellationToken);
        if (key == null)
            return await ErrorAsync(req, HttpStatusCode.NotFound, "No API key found. Use POST /api/token to generate one.", "NOT_FOUND");

        return await JsonAsync(req, HttpStatusCode.OK, new { apiKey = key, githubLogin = identity.Login });
    }

    /// <summary>
    /// POST /api/token — issues or retrieves a long-lived API key for the authenticated user.
    /// </summary>
    [Function("TokenPost")]
    public async Task<HttpResponseData> PostToken(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "token")] HttpRequestData req,
        FunctionContext executionContext)
    {
        var identity = GetIdentity(req);
        if (identity == null)
            return await ErrorAsync(req, HttpStatusCode.Unauthorized, "Authentication required.", "UNAUTHENTICATED");

        // Return existing key if one already exists (idempotent)
        var existing = await _store.GetKeyAsync(identity.UserId, executionContext.CancellationToken);
        if (existing != null)
            return await JsonAsync(req, HttpStatusCode.OK, new { apiKey = existing, githubLogin = identity.Login, created = false });

        var newKey = await _store.CreateKeyAsync(identity.UserId, identity.Login, executionContext.CancellationToken);
        return await JsonAsync(req, HttpStatusCode.Created, new { apiKey = newKey, githubLogin = identity.Login, created = true });
    }

    private EasyAuthIdentity? GetIdentity(HttpRequestData req)
    {
        if (_isDevelopment)
            return DevIdentity;

        req.Headers.TryGetValues("x-ms-client-principal", out var values);
        var header = values != null ? string.Join("", values) : null;
        return EasyAuthPrincipal.Parse(header);
    }

    private static async Task<HttpResponseData> JsonAsync<T>(HttpRequestData req, HttpStatusCode status, T body)
    {
        var response = req.CreateResponse();
        response.StatusCode = status;
        response.Headers.Add("Content-Type", "application/json; charset=utf-8");
        await response.WriteStringAsync(JsonSerializer.Serialize(body));
        return response;
    }

    private static async Task<HttpResponseData> ErrorAsync(HttpRequestData req, HttpStatusCode status, string message, string errorCode)
    {
        return await JsonAsync(req, status, new { error = message, errorCode });
    }
}