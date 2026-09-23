using System;
using camonlinux.Imaging;
using Xunit;

namespace camonlinux.Tests;

/// <summary>
/// The alpha helpers run over every byte of every captured frame, and their SIMD path
/// processes whole <c>Vector&lt;byte&gt;</c> blocks with a scalar tail — exactly the shape
/// of code where an off-by-one in the tail silently corrupts the last few pixels.
/// These tests compare the vectorised result against a naive reference implementation
/// across sizes that straddle the vector width and the tail.
/// </summary>
public class PixelBufferTests
{
    [Fact]
    public void SetAlpha_TouchesOnlyTheAlphaLanes()
    {
        // 3 pixels; RGB values are distinctive so any lane bleed is caught.
        var data = new byte[] { 1, 2, 3, 0, 4, 5, 6, 0, 7, 8, 9, 0 };

        PixelBuffer.SetAlpha(data, data.Length, 0xAB);

        Assert.Equal(
            new byte[] { 1, 2, 3, 0xAB, 4, 5, 6, 0xAB, 7, 8, 9, 0xAB },
            data);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(7)]
    [InlineData(8)]
    [InlineData(9)]
    [InlineData(16)]
    [InlineData(33)]
    [InlineData(64)]
    [InlineData(255)]
    [InlineData(1024)]
    public void SetAlpha_MatchesReferenceImplementation(int pixels)
    {
        var data = CreatePixelData(pixels);
        var expected = (byte[])data.Clone();
        for (var p = 0; p < pixels; p++)
            expected[p * 4 + 3] = 0x5A;

        PixelBuffer.SetAlpha(data, data.Length, 0x5A);

        Assert.Equal(expected, data);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(9)]
    [InlineData(64)]
    [InlineData(257)]
    public void InvertAlpha_MatchesReferenceImplementation(int pixels)
    {
        var data = CreatePixelData(pixels);
        var expected = (byte[])data.Clone();
        for (var p = 0; p < pixels; p++)
            expected[p * 4 + 3] = (byte)(255 - expected[p * 4 + 3]);

        PixelBuffer.InvertAlpha(data, data.Length);

        Assert.Equal(expected, data);
    }

    [Fact]
    public void InvertAlpha_TwiceIsIdentity()
    {
        var data = CreatePixelData(37);
        var original = (byte[])data.Clone();

        PixelBuffer.InvertAlpha(data, data.Length);
        PixelBuffer.InvertAlpha(data, data.Length);

        Assert.Equal(original, data);
    }

    [Fact]
    public void SetAlpha_DoesNotWritePastByteLength()
    {
        // 10 pixels, only the first 4 should be touched.
        var data = new byte[40];
        Array.Fill(data, (byte)0x11);

        PixelBuffer.SetAlpha(data, byteLength: 16, alpha: 0xFF);

        for (var p = 0; p < 4; p++)
            Assert.Equal(0xFF, data[p * 4 + 3]);
        for (var p = 4; p < 10; p++)
            Assert.Equal(0x11, data[p * 4 + 3]);
    }

    [Fact]
    public void SetAlpha_ClampsByteLengthLargerThanTheBuffer()
    {
        // A stale/oversized byte count must not read or write out of bounds.
        var data = new byte[16];

        PixelBuffer.SetAlpha(data, byteLength: 4096, alpha: 0xFF);

        for (var p = 0; p < 4; p++)
            Assert.Equal(0xFF, data[p * 4 + 3]);
    }

    [Fact]
    public void SetAlpha_HandlesATrailingPartialPixel()
    {
        // byteLength 10 = 2 whole pixels + 2 leftover bytes; only whole pixels change.
        var data = new byte[] { 1, 2, 3, 0, 4, 5, 6, 0, 7, 8 };

        PixelBuffer.SetAlpha(data, data.Length, 0xFF);

        Assert.Equal(new byte[] { 1, 2, 3, 0xFF, 4, 5, 6, 0xFF, 7, 8 }, data);
    }

    [Fact]
    public void SetOpaque_DefaultsToTheWholeBuffer()
    {
        var data = CreatePixelData(19);

        PixelBuffer.SetOpaque(data);

        for (var p = 0; p < 19; p++)
            Assert.Equal(0xFF, data[p * 4 + 3]);
    }

    [Fact]
    public void EmptyBuffer_IsANoOp()
    {
        var data = Array.Empty<byte>();

        PixelBuffer.SetAlpha(data, 0, 0xFF);
        PixelBuffer.InvertAlpha(data, 0);

        Assert.Empty(data);
    }

    // ------------------------------------------------------------------ //
    // FlattenOverBackground
    // ------------------------------------------------------------------ //

    [Fact]
    public void FlattenOverBackground_CopiesOpaquePixelsVerbatim()
    {
        // With no mask active every pixel is opaque, so a photo must be untouched.
        var source = new byte[] { 10, 20, 30, 255, 40, 50, 60, 255 };
        var destination = new byte[source.Length];
        Array.Fill(destination, (byte)0xEE);

        PixelBuffer.FlattenOverBackground(source, destination, source.Length, 0, 0, 0);

        Assert.Equal(source, destination);
    }

    [Fact]
    public void FlattenOverBackground_FillsCutOutPixelsWithTheBackground()
    {
        // 1 pixel, fully cut away by the mask; background is green (R=0,G=255,B=0).
        var source = new byte[] { 200, 100, 50, 0 };
        var destination = new byte[4];

        PixelBuffer.FlattenOverBackground(source, destination, source.Length, 0, 255, 0);

        // BGRA order: B and R come from the background, G too; alpha is forced opaque.
        Assert.Equal(new byte[] { 0, 255, 0, 255 }, destination);
    }

    [Fact]
    public void FlattenOverBackground_BlendsSemiTransparentPixels()
    {
        // Feather/soft masks produce partial alpha; those pixels must be blended, not
        // replaced. Background red (R=255) => B=0, G=0, R=255.
        var source = new byte[] { 200, 100, 50, 128 };
        var destination = new byte[4];

        PixelBuffer.FlattenOverBackground(source, destination, source.Length, 255, 0, 0);

        Assert.Equal((byte)((200 * 128 + 0 * 127) / 255), destination[0]);
        Assert.Equal((byte)((100 * 128 + 0 * 127) / 255), destination[1]);
        Assert.Equal((byte)((50 * 128 + 255 * 127) / 255), destination[2]);
        Assert.Equal(255, destination[3]);
    }

    [Fact]
    public void FlattenOverBackground_AlwaysProducesOpaquePixels()
    {
        var source = new byte[] { 1, 2, 3, 0, 4, 5, 6, 1, 7, 8, 9, 254, 10, 11, 12, 255 };
        var destination = new byte[source.Length];

        PixelBuffer.FlattenOverBackground(source, destination, source.Length, 0, 0, 0);

        for (var p = 0; p < 4; p++)
            Assert.Equal(255, destination[p * 4 + 3]);
    }

    [Fact]
    public void FlattenOverBackground_StopsAtTheShorterBuffer()
    {
        // A destination smaller than the requested length must be clamped, not overrun.
        var source = new byte[16];
        Array.Fill(source, (byte)0xFF);
        var destination = new byte[8];

        PixelBuffer.FlattenOverBackground(source, destination, source.Length, 0, 0, 0);

        Assert.Equal(new byte[] { 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, destination);
    }

    [Fact]
    public void FlattenOverBackground_ClampsAnOversizedByteLength()
    {
        var source = new byte[] { 1, 2, 3, 255 };
        var destination = new byte[4096];

        PixelBuffer.FlattenOverBackground(source, destination, byteLength: 9999, r: 0, g: 0, b: 0);

        Assert.Equal(new byte[] { 1, 2, 3, 255 }, destination[..4]);
        Assert.Equal(0, destination[4]); // untouched
    }

    [Fact]
    public void FlattenOverBackground_EmptySource_IsANoOp()
    {
        var destination = new byte[4];

        PixelBuffer.FlattenOverBackground(Array.Empty<byte>(), destination, 0, 0, 0, 0);

        Assert.Equal(new byte[4], destination);
    }

    /// <summary>Builds a deterministic BGRA buffer whose RGB bytes are non-trivial.</summary>
    private static byte[] CreatePixelData(int pixels)
    {
        var data = new byte[pixels * 4];
        for (var i = 0; i < data.Length; i++)
            data[i] = (byte)(i * 7 + 1);
        return data;
    }
}
