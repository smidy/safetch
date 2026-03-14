using System;
using System.Text.Json;
using Safetch.Core.Auth;
using Xunit;

namespace Safetch.Tests.Auth;

public class EasyAuthPrincipalTests
{
    [Fact]
    public void Parse_NullInput_ReturnsNull()
    {
        var result = EasyAuthPrincipal.Parse(null);
        Assert.Null(result);
    }

    [Fact]
    public void Parse_ValidHeader_ReturnsIdentity()
    {
        var header = BuildPrincipalHeader("123", "octocat");
        var result = EasyAuthPrincipal.Parse(header);

        Assert.NotNull(result);
        Assert.Equal("123", result.UserId);
        Assert.Equal("octocat", result.Login);
    }

    [Fact]
    public void Parse_MissingNameIdentifierClaim_ReturnsNull()
    {
        var json = "{\"auth_typ\": \"github\", \"claims\": [{ \"typ\": \"urn:github:login\", \"val\": \"octocat\" }]}";
        var header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        var result = EasyAuthPrincipal.Parse(header);

        Assert.Null(result);
    }

    [Fact]
    public void Parse_EmptyClaimsArray_ReturnsNull()
    {
        var json = "{\"auth_typ\": \"github\", \"claims\": []}";
        var header = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
        var result = EasyAuthPrincipal.Parse(header);

        Assert.Null(result);
    }

    private static string BuildPrincipalHeader(string userId, string login)
    {
        var json = "{\"auth_typ\": \"github\", \"claims\": [{ \"typ\": \"http://schemas.xmlsoap.org/ws/2005/05/identity/claims/nameidentifier\", \"val\": \"" + userId + "\" }, { \"typ\": \"urn:github:login\", \"val\": \"" + login + "\" }]}";
        return Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(json));
    }
}