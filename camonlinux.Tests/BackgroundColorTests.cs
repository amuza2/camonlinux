using camonlinux.Models;
using Xunit;

namespace camonlinux.Tests;

/// <summary>
/// One setting drives the background behind a mask for both the virtual webcam and
/// captured photos, so the name-to-colour mapping is shared and worth pinning down.
/// </summary>
public class BackgroundColorTests
{
    [Theory]
    [InlineData("Black", 0, 0, 0)]
    [InlineData("Green", 0, 255, 0)]
    [InlineData("White", 255, 255, 255)]
    [InlineData("  green  ", 0, 255, 0)]
    [InlineData("GREEN", 0, 255, 0)]
    [InlineData("white", 255, 255, 255)]
    public void Parse_MapsKnownNames(string name, byte r, byte g, byte b)
    {
        Assert.Equal((r, g, b), BackgroundColor.Parse(name));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("mauve")]
    public void Parse_FallsBackToBlack(string? name)
    {
        Assert.Equal(BackgroundColor.Black, BackgroundColor.Parse(name));
    }
}
