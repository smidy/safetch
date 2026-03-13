using System.Collections.Generic;
using Microsoft.Azure.Functions.Worker.Http;

namespace Safetch.Tests.Fakes;

public class FakeHttpCookies : HttpCookies
{
    public override void Append(string name, string value) { }
    public override void Append(IHttpCookie cookie) { }
    public override IHttpCookie CreateNew() => null!;
}