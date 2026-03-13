using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Claims;
using System.Text;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Safetch.Tests.Fakes;

public class FakeHttpRequestData : HttpRequestData
{
    private readonly string _body;
    private readonly string _method;
    private readonly string _url;

    public FakeHttpRequestData(
        FunctionContext context,
        string body = "",
        string method = "POST",
        string url = "http://localhost/api/fetch")
        : base(context)
    {
        _body = body;
        _method = method;
        _url = url;
    }

    public override Stream Body => new MemoryStream(Encoding.UTF8.GetBytes(_body));
    public override HttpHeadersCollection Headers => new HttpHeadersCollection();
    public override IReadOnlyCollection<IHttpCookie> Cookies => Array.Empty<IHttpCookie>();
    public override Uri Url => new Uri(_url);
    public override IEnumerable<ClaimsIdentity> Identities => Array.Empty<ClaimsIdentity>();
    public override string Method => _method;
    public override HttpResponseData CreateResponse() => new FakeHttpResponseData(FunctionContext);
}