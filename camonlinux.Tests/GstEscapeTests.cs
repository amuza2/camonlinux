using camonlinux.Capture;
using Xunit;

namespace camonlinux.Tests;

/// <summary>
/// <c>gst_parse_launch</c> terminates a quoted string at the next double quote, so a
/// stray quote in a folder or microphone name would truncate the pipeline (or inject
/// extra elements into it). These tests pin down the escaping contract.
/// </summary>
public class GstEscapeTests
{
    [Theory]
    [InlineData("plain.mkv", "\"plain.mkv\"")]
    [InlineData("/home/user/Videos/video.mkv", "\"/home/user/Videos/video.mkv\"")]
    [InlineData("/home/my folder/a b.mkv", "\"/home/my folder/a b.mkv\"")]
    [InlineData("say \"hi\"", "\"say \\\"hi\\\"\"")]
    [InlineData("back\\slash", "\"back\\\\slash\"")]
    [InlineData("\"", "\"\\\"\"")]
    [InlineData("", "\"\"")]
    public void Quote_WrapsAndEscapes(string input, string expected)
    {
        Assert.Equal(expected, GstEscape.Quote(input));
    }

    [Fact]
    public void Quote_NullBecomesAnEmptyQuotedString()
    {
        Assert.Equal("\"\"", GstEscape.Quote(null));
    }

    [Fact]
    public void Property_FormatsNameAndQuotedValue()
    {
        Assert.Equal("location=\"/tmp/a.mkv\"", GstEscape.Property("location", "/tmp/a.mkv"));
    }

    [Fact]
    public void Property_EscapesAValueThatTriesToBreakOutOfTheString()
    {
        // Without escaping this would close the quote and splice a new element into
        // the pipeline description.
        var injected = "x\" ! fakesink name=evil ! \"";

        var result = GstEscape.Property("device", injected);

        Assert.Equal("device=\"x\\\" ! fakesink name=evil ! \\\"\"", result);
        // Only the wrapping pair may remain unescaped, so the value can't terminate
        // the string early — everything the value contributed is escaped.
        Assert.Equal(2, CountUnescapedQuotes(result));
    }

    /// <summary>Counts double quotes that are not preceded by a backslash.</summary>
    private static int CountUnescapedQuotes(string value)
    {
        var count = 0;
        for (var i = 0; i < value.Length; i++)
        {
            if (value[i] == '"' && (i == 0 || value[i - 1] != '\\'))
                count++;
        }
        return count;
    }
}
