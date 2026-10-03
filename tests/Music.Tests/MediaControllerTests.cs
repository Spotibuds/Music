using Music.Controllers;
using Music.Services;
namespace Music.Tests;
public class MediaControllerTests
{
    [Theory]
    [InlineData("bytes=0-0", 10, 0, 1)]
    [InlineData("bytes=-3", 10, 7, 3)]
    [InlineData("bytes=3-", 10, 3, 7)]
    [InlineData("bytes=0-100", 10, 0, 10)]
    public void ExactByteRanges(string range, long total, long expectedStart, long expectedLength)
    {
        Assert.True(MediaController.TryRange(range, total, out var start, out var length));
        Assert.Equal(expectedStart, start); Assert.Equal(expectedLength, length);
    }
    [Theory]
    [InlineData("bytes=20-30")][InlineData("bytes=-0")][InlineData("bytes=garbage-1")][InlineData("bytes=2-1")][InlineData("bytes=0-1,3-4")]
    public void InvalidRangesAreRejected(string range) => Assert.False(MediaController.TryRange(range, 10, out _, out _));
    [Fact] public void WaveParserChecksEntirePayloadAndDuration()
    {
        Assert.Equal(1, AzureBlobService.ValidateWave(DemoFactory.Wave()));
        Assert.Throws<ApiException>(() => AzureBlobService.ValidateWave(DemoFactory.Wave()[..^1]));
        Assert.Throws<ApiException>(() => AzureBlobService.ValidateWave(new byte[44]));
    }
}
