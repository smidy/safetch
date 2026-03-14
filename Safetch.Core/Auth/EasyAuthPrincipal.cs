using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Safetch.Core.Auth;

/// <summary>
/// Parses the X-MS-CLIENT-PRINCIPAL header set by Azure Easy Auth.
/// The header value is a base64-encoded JSON object.
/// </summary>
public static class EasyAuthPrincipal
{
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
            var principal = JsonSerializer.Deserialize<ClientPrincipal>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (principal?.Claims == null)
                return null;

            string? userId = null;
            string? login = null;

            foreach (var claim in principal.Claims)
            {
                if (claim.Typ == NameIdClaimType)
                    userId = claim.Val;
                else if (claim.Typ == GitHubLoginClaimType)
                    login = claim.Val;
            }

            if (userId == null)
                return null;

            return new EasyAuthIdentity(userId, login ?? userId);
        }
        catch
        {
            return null;
        }
    }

    private record ClientPrincipal(
        [property: JsonPropertyName("auth_typ")] string? AuthTyp,
        [property: JsonPropertyName("claims")] List<ClaimEntry>? Claims
    );

    private record ClaimEntry(
        [property: JsonPropertyName("typ")] string Typ,
        [property: JsonPropertyName("val")] string Val
    );
}

public record EasyAuthIdentity(string UserId, string Login);