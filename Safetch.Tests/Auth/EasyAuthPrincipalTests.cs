using System;
using Safetch.Core.Auth;
using Xunit;

namespace Safetch.Tests.Auth;

public class EasyAuthPrincipalTests
{
    // ── Shared helpers ─────────────────────────────────────────────────────

    /// <summary>SWA backend link format (flat JSON with userId + userDetails).</summary>
    private static string BuildSwaHeader(string userId, string userDetails)
    {
        var json = $"{{\"identityProvider\":\"github\",\"userId\":\"{userId}\",\"userDetails\":\"{userDetails}\",\"userRoles\":[\"authenticated\",\"anonymous\"]}}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    /// <summary>App Service Easy Auth format (claims array with typ/val pairs).</summary>
    private static string BuildEasyAuthHeader(string userId, string login)
    {
        var json = "{\"auth_typ\": \"github\", \"claims\": [{ \"typ\": \"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier\", \"val\": \"" + userId + "\" }, { \"typ\": \"urn:github:login\", \"val\": \"" + login + "\" }]}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }

    // ── Null / empty inputs ────────────────────────────────────────────────

    [Fact]
    public void Parse_NullInput_ReturnsNull()
    {
        Assert.Null(EasyAuthPrincipal.Parse(null));
    }

    [Fact]
    public void Parse_EmptyString_ReturnsNull()
    {
        Assert.Null(EasyAuthPrincipal.Parse(""));
    }

    [Fact]
    public void Parse_InvalidBase64_ReturnsNull()
    {
        Assert.Null(EasyAuthPrincipal.Parse("not-valid-base64!!!"));
    }

    // ── SWA backend link format ────────────────────────────────────────────

    [Fact]
    public void Parse_SwaFormat_ReturnsCorrectIdentity()
    {
        var header = BuildSwaHeader("10979408", "smidy");
        var result = EasyAuthPrincipal.Parse(header);

        Assert.NotNull(result);
        Assert.Equal("10979408", result!.UserId);
        Assert.Equal("smidy", result.Login);
    }

    [Fact]
    public void Parse_SwaFormat_RealTokenFromLogs_ReturnsIdentity()
    {
        // Actual token observed in Azure logs
        const string realToken = "eyJpZGVudGl0eVByb3ZpZGVyIjoiZ2l0aHViIiwidXNlcklkIjoiMTA5Nzk0MDgiLCJ1c2VyRGV0YWlscyI6InNtaWR5IiwidXNlclJvbGVzIjpbImF1dGhlbnRpY2F0ZWQiLCJhbm9ueW1vdXMiXX0=";
        var result = EasyAuthPrincipal.Parse(realToken);

        Assert.NotNull(result);
        Assert.Equal("10979408", result!.UserId);
        Assert.Equal("smidy", result.Login);
    }

    [Fact]
    public void Parse_SwaFormat_MissingUserId_ReturnsNull()
    {
        var json = "{\"identityProvider\":\"github\",\"userDetails\":\"smidy\",\"userRoles\":[\"authenticated\"]}";
        var header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Null(EasyAuthPrincipal.Parse(header));
    }

    // ── App Service Easy Auth format ───────────────────────────────────────

    [Fact]
    public void Parse_EasyAuthFormat_ReturnsCorrectIdentity()
    {
        var header = BuildEasyAuthHeader("123", "octocat");
        var result = EasyAuthPrincipal.Parse(header);

        Assert.NotNull(result);
        Assert.Equal("123", result!.UserId);
        Assert.Equal("octocat", result.Login);
    }

    [Fact]
    public void Parse_EasyAuthFormat_MissingNameIdentifierClaim_ReturnsNull()
    {
        var json = "{\"auth_typ\": \"github\", \"claims\": [{ \"typ\": \"urn:github:login\", \"val\": \"octocat\" }]}";
        var header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Null(EasyAuthPrincipal.Parse(header));
    }

    [Fact]
    public void Parse_EasyAuthFormat_EmptyClaimsArray_ReturnsNull()
    {
        var json = "{\"auth_typ\": \"github\", \"claims\": []}";
        var header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));

        Assert.Null(EasyAuthPrincipal.Parse(header));
    }
}
