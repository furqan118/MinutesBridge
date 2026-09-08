using MinutesBridge.Core.Authentication;

namespace MinutesBridge.Core.Tests;

public sealed class BrokerOptionsTests
{
    [Theory]
    [InlineData("http://broker.example/")]
    [InlineData("https://user:password@broker.example/")]
    [InlineData("https://broker.example/path/")]
    [InlineData("https://broker.example/?secret=value")]
    public void ValidateRejectsUnsafeBrokerAddresses(string value)
    {
        Assert.Throws<ArgumentException>(() => BrokerOptions.Validate(new Uri(value)));
    }

    [Fact]
    public void ValidateAcceptsHttpsOrigin()
    {
        BrokerOptions.Validate(new Uri("https://broker.example/"));
    }
}
