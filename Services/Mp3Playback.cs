namespace Music.Services;

/// <summary>Removes leading ID3 artwork/metadata while preserving every MPEG audio byte.</summary>
public static class Mp3Playback
{
    public static byte[] AudioOnly(byte[] source)
    {
        var offset = 0;
        while (source.AsSpan(offset).StartsWith("ID3"u8))
        {
            var header = source.AsSpan(offset);
            if (header.Length < 10 || header[3] is < 2 or > 4 || header[4] == 255)
                throw new ApiException(400, "Invalid MP3 metadata header.");
            var size = 0;
            for (var i = 6; i < 10; i++)
            {
                if (header[i] >= 128) throw new ApiException(400, "Invalid MP3 metadata size.");
                size = (size << 7) | header[i];
            }
            var footer = header[3] == 4 && (header[5] & 16) != 0;
            var length = 10L + size + (footer ? 10 : 0);
            if (length > header.Length) throw new ApiException(400, "Truncated MP3 metadata.");
            if (footer && (!header.Slice(10 + size, 3).SequenceEqual("3DI"u8)
                || !header.Slice(13 + size, 7).SequenceEqual(header.Slice(3, 7))))
                throw new ApiException(400, "Invalid MP3 metadata footer.");
            offset += (int)length;
        }
        if (offset == source.Length) throw new ApiException(400, "MP3 contains no audio.");
        // Keep encoder/seek information in the MPEG frames, including the Xing/Info frame.
        // Catalogue titles and covers are stored separately; source downloads stay untouched.
        return offset == 0 ? source : source[offset..];
    }
}
