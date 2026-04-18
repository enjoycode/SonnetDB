using Xunit;

namespace TSLite.Tests;

/// <summary>
/// 烟雾测试：验证 CI 通路正常。
/// </summary>
public sealed class SmokeTests
{
    [Fact]
    public void Smoke_Always_Passes()
    {
        Assert.True(true);
    }
}
