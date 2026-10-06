using MiniBrowser.Services;
using Xunit;

public class AddressCopyTests
{
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void CanCopy_EmptyUrl_ReturnsFalse(string? url)
    {
        Assert.False(AddressCopy.CanCopy(url));
    }

    [Theory]
    [InlineData("https://www.google.com/")]
    [InlineData("example.com")]
    public void CanCopy_NonEmptyUrl_ReturnsTrue(string? url)
    {
        Assert.True(AddressCopy.CanCopy(url));
    }
}
