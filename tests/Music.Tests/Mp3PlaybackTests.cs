using Music.Services;

namespace Music.Tests;

public class Mp3PlaybackTests
{
    private static readonly byte[] Audio = [0xff, 0xfb, 0xe0, 0x64, 0, 1, 2, 3, 4, 5];

    private static byte[] Tag(int version, int size, bool footer = false)
    {
        var header = new byte[] { 73, 68, 51, (byte)version, 0, footer ? (byte)16 : (byte)0,
            (byte)((size >> 21) & 127), (byte)((size >> 14) & 127), (byte)((size >> 7) & 127), (byte)(size & 127) };
        byte[] ending = footer ? [51, 68, 73, .. header[3..]] : [];
        return [.. header, .. new byte[size], .. ending];
    }

    [Theory]
    [InlineData(2, 0, false)]
    [InlineData(3, 116276, false)]
    [InlineData(4, 1024, false)]
    [InlineData(4, 1024, true)]
    public void RemovesMetadataAndPreservesAudioExactly(int version, int size, bool footer)
    {
        byte[] original = [.. Tag(version, size, footer), .. Audio];
        var saved = original.ToArray();
        Assert.Equal(Audio, Mp3Playback.AudioOnly(original));
        Assert.Equal(saved, original);
    }

    [Fact]
    public void HandlesConsecutiveTagsAndAlreadyOptimizedAudio()
    {
        Assert.Equal(Audio, Mp3Playback.AudioOnly([.. Tag(3, 10), .. Tag(4, 20, true), .. Audio]));
        Assert.Same(Audio, Mp3Playback.AudioOnly(Audio));
    }

    [Fact]
    public void RejectsTruncatedOversizedAndMalformedTags()
    {
        Assert.Throws<ApiException>(() => Mp3Playback.AudioOnly("ID3"u8.ToArray()));
        Assert.Throws<ApiException>(() => Mp3Playback.AudioOnly(Tag(3, 32)[..^1]));
        byte[] invalidSize = [.. Tag(3, 0), .. Audio];
        invalidSize[6] = 128;
        Assert.Throws<ApiException>(() => Mp3Playback.AudioOnly(invalidSize));
        byte[] invalidFooter = [.. Tag(4, 0, true), .. Audio];
        invalidFooter[10] = 0;
        Assert.Throws<ApiException>(() => Mp3Playback.AudioOnly(invalidFooter));
        Assert.Throws<ApiException>(() => Mp3Playback.AudioOnly(Tag(3, 0)));
        Assert.Throws<ApiException>(() => Mp3Playback.AudioOnly([]));
    }
}
