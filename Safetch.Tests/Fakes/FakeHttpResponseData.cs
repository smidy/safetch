using System.IO;
using System.Net;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Azure.Functions.Worker.Http;

namespace Safetch.Tests.Fakes;

public class FakeHttpResponseData : HttpResponseData
{
    public FakeHttpResponseData(FunctionContext context) : base(context)
    {
        Body = new MemoryStream();
        Headers = new HttpHeadersCollection();
    }

    public override HttpStatusCode StatusCode { get; set; }
    public override HttpHeadersCollection Headers { get; set; }
    public override Stream Body { get; set; }
    public override HttpCookies Cookies => new FakeHttpCookies();
}