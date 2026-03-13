using System;
using System.Collections.Generic;
using Microsoft.Azure.Functions.Worker;
using Microsoft.Extensions.DependencyInjection;

namespace Safetch.Tests.Fakes;

public class FakeFunctionContext : FunctionContext
{
    private readonly IServiceProvider _services;

    public FakeFunctionContext()
    {
        var sc = new ServiceCollection();
        // No services needed for testing
        _services = sc.BuildServiceProvider();
    }

    public override string InvocationId => "test-invocation";
    public override string FunctionId => "test-function";
    public override TraceContext TraceContext => null!;
    public override BindingContext BindingContext => null!;
    public override RetryContext RetryContext => null!;
    public override IServiceProvider InstanceServices { get => _services; set { } }
    public override FunctionDefinition FunctionDefinition => null!;
    public override IDictionary<object, object> Items { get; set; } = new Dictionary<object, object>();
    public override IInvocationFeatures Features => null!;
}