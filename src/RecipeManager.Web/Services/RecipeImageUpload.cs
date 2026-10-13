namespace RecipeManager.Web.Services;

public static class RecipeImageUpload
{
    public const long MaximumBytes = 10 * 1024 * 1024;

    public static bool IsWithinSizeLimit(long length) => length is > 0 and <= MaximumBytes;

    public static bool HasSupportedSignature(ReadOnlySpan<byte> bytes)
    {
        var isJpeg = bytes.Length >= 3
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF;

        var isPng = bytes.Length >= 8
            && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A });

        var isWebp = bytes.Length >= 12
            && bytes[..4].SequenceEqual("RIFF"u8)
            && bytes[8..12].SequenceEqual("WEBP"u8);

        return isJpeg || isPng || isWebp;
    }
}
