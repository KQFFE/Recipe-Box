namespace RecipeManager.Web.Services;

public static class RecipeImageUpload
{
    public const long MaximumBytes = 10 * 1024 * 1024;

    public static bool IsWithinSizeLimit(long length) => length is > 0 and <= MaximumBytes;

    public static bool HasSupportedSignature(ReadOnlySpan<byte> bytes)
    {
        return GetContentType(bytes) is not null;
    }

    public static string? GetContentType(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length >= 3
            && bytes[0] == 0xFF
            && bytes[1] == 0xD8
            && bytes[2] == 0xFF)
        {
            return "image/jpeg";
        }

        if (bytes.Length >= 8
            && bytes[..8].SequenceEqual(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return "image/png";
        }

        if (bytes.Length >= 12
            && bytes[..4].SequenceEqual("RIFF"u8)
            && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    public static async Task<byte[]?> ReadAsync(IFormFile? upload, CancellationToken cancellationToken)
    {
        if (upload is null || upload.Length == 0)
        {
            return null;
        }

        if (!IsWithinSizeLimit(upload.Length))
        {
            throw new InvalidDataException("Choose an image smaller than 10 MB.");
        }

        await using var buffer = new MemoryStream((int)upload.Length);
        await upload.CopyToAsync(buffer, cancellationToken);
        var imageBytes = buffer.ToArray();
        if (!IsWithinSizeLimit(imageBytes.Length))
        {
            throw new InvalidDataException("Choose an image smaller than 10 MB.");
        }

        if (!HasSupportedSignature(imageBytes))
        {
            throw new InvalidDataException("Upload a valid JPEG, PNG, or WebP image.");
        }

        return imageBytes;
    }
}
