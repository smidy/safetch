using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Safetch.Core.Auth;

namespace Safetch.Api.Functions;

public class TokenFunction
{
    private readonly IApiKeyStore _store;
    private readonly bool _isDevelopment;
    private readonly ILogger<TokenFunction> _logger;

    // Fake identity used when running locally in Development mode
    private static readonly EasyAuthIdentity DevIdentity = new("dev-user", "local-dev");

    public TokenFunction(IApiKeyStore store, IHostEnvironment environment, ILogger<TokenFunction> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _isDevelopment = environment?.IsDevelopment() ?? false;
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
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
    /// Optional query parameter "regenerate=true" deletes any existing key before generating a new one.
    /// </summary>
    [Function("TokenPost")]
    public async Task<HttpResponseData> PostToken(
        [HttpTrigger(AuthorizationLevel.Anonymous, "post", Route = "token")] HttpRequestData req,
        FunctionContext executionContext)
    {
        var identity = GetIdentity(req);
        if (identity == null)
            return await ErrorAsync(req, HttpStatusCode.Unauthorized, "Authentication required.", "UNAUTHENTICATED");

        // Parse query parameters
        var query = req.Url.Query.TrimStart('?');
        var parameters = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var idx = part.IndexOf('=');
            if (idx < 0) continue;
            var key = Uri.UnescapeDataString(part[..idx]);
            var val = Uri.UnescapeDataString(part[(idx + 1)..]);
            parameters[key] = val;
        }

        bool regenerate = parameters.TryGetValue("regenerate", out var regenerateValue) &&
                          string.Equals(regenerateValue, "true", StringComparison.OrdinalIgnoreCase);

        if (regenerate)
        {
            // Delete existing key if any
            await _store.DeleteKeyAsync(identity.UserId, executionContext.CancellationToken);
        }

        // Return existing key if one already exists and we are not regenerating
        var existing = await _store.GetKeyAsync(identity.UserId, executionContext.CancellationToken);
        if (existing != null && !regenerate)
            return await JsonAsync(req, HttpStatusCode.OK, new { apiKey = existing, githubLogin = identity.Login, created = false });

        var newKey = await _store.CreateKeyAsync(identity.UserId, identity.Login, executionContext.CancellationToken);
        return await JsonAsync(req, HttpStatusCode.Created, new { apiKey = newKey, githubLogin = identity.Login, created = true });
    }

    private EasyAuthIdentity? GetIdentity(HttpRequestData req)
    {
        if (_isDevelopment)
            return DevIdentity;

        // Log all incoming headers to diagnose missing X-MS-CLIENT-PRINCIPAL
        _logger.LogInformation("TokenFunction incoming headers: {Headers}",
            string.Join(", ", req.Headers.Select(h => $"{h.Key}=[{string.Join("|", h.Value)}]")));

        req.Headers.TryGetValues("x-ms-client-principal", out var values);
        var header = values != null ? string.Join("", values) : null;

        _logger.LogInformation("X-MS-CLIENT-PRINCIPAL present: {Present}, value length: {Length}",
            header != null, header?.Length ?? 0);

        var identity = EasyAuthPrincipal.Parse(header);
        _logger.LogInformation("Parsed identity — UserId: {UserId}, Login: {Login}",
            identity?.UserId ?? "(null)", identity?.Login ?? "(null)");

        return identity;
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