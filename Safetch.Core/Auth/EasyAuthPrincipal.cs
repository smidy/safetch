using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Safetch.Core.Auth;

/// <summary>
/// Parses the X-MS-CLIENT-PRINCIPAL header injected by Azure Static Web Apps or App Service Easy Auth.
///
/// SWA backend link format (used when Function App is linked as SWA backend):
/// { "identityProvider": "github", "userId": "...", "userDetails": "...", "userRoles": [...] }
///
/// App Service Easy Auth format (used when Easy Auth is configured directly on the Function App):
/// { "auth_typ": "github", "claims": [ { "typ": "...", "val": "..." }, ... ] }
///
/// Both formats are handled — SWA format is attempted first.
/// </summary>
public static class EasyAuthPrincipal
{
    // App Service Easy Auth claim types
    private const string NameIdClaimType =
        "http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier";
    private const string GitHubLoginClaimType = "urn:github:login";

    public static EasyAuthIdentity? Parse(string? base64Header)
    {
        if (string.IsNullOrWhiteSpace(base64Header))
            return null;

        try
        {
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64Header));
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;

            // SWA backend link format: flat object with userId + userDetails
            if (root.TryGetProperty("userId", out var userIdProp) &&
                root.TryGetProperty("userDetails", out var userDetailsProp))
            {
                var userId = userIdProp.GetString();
                var login = userDetailsProp.GetString();
                if (!string.IsNullOrWhiteSpace(userId))
                    return new EasyAuthIdentity(userId, login ?? userId);
            }

            // App Service Easy Auth format: claims array with typ/val pairs
            if (root.TryGetProperty("claims", out var claimsProp) &&
                claimsProp.ValueKind == JsonValueKind.Array)
            {
                string? userId = null;
                string? login = null;

                foreach (var claim in claimsProp.EnumerateArray())
                {
                    if (!claim.TryGetProperty("typ", out var typ) ||
                        !claim.TryGetProperty("val", out var val))
                        continue;

                    var typStr = typ.GetString();
                    var valStr = val.GetString();

                    if (typStr == NameIdClaimType)
                        userId = valStr;
                    else if (typStr == GitHubLoginClaimType)
                        login = valStr;
                }

                if (!string.IsNullOrWhiteSpace(userId))
                    return new EasyAuthIdentity(userId, login ?? userId);
            }

            return null;
        }
        catch
        {
            return null;
        }
    }
}

public record EasyAuthIdentity(string UserId, string Login);
