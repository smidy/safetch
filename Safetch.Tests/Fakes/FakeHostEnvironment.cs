using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;

namespace Safetch.Tests.Fakes;

/// <summary>
/// Minimal IHostEnvironment for use in unit tests.
/// Defaults to Production (IsDevelopment() returns false).
/// Use FakeHostEnvironment("Development") to simulate local dev mode.
/// </summary>
public class FakeHostEnvironment : IHostEnvironment
{
    public FakeHostEnvironment(string environmentName = "Production")
    {
        EnvironmentName = environmentName;
    }

    public string EnvironmentName { get; set; }
    public string ApplicationName { get; set; } = "Safetch.Tests";
    public string ContentRootPath { get; set; } = "/";
    public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
}
