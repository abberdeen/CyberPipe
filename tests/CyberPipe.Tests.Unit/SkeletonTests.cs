using System.Reflection;
using Xunit;

namespace CyberPipe.Tests.Unit;

public sealed class SkeletonTests
{
    [Fact]
    public void ContractsAssembly_IsLoadable()
    {
        Assert.Equal("CyberPipe.Contracts", Assembly.Load("CyberPipe.Contracts").GetName().Name);
    }
}
