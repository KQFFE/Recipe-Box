namespace RecipeBox.Mobile;

public static class PhotoReader
{
    private const int MaximumBytes = 10 * 1024 * 1024;

    public static async Task<(byte[] Bytes, string ContentType, string FileName)> ReadAsync(
        Stream photoStream)
    {
        await using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        while (true)
        {
            var bytesRead = await photoStream.ReadAsync(chunk);
            if (bytesRead == 0)
            {
                break;
            }

            if (buffer.Length + bytesRead > MaximumBytes)
            {
                throw new InvalidDataException("Choose a photo smaller than 10 MB.");
            }

            await buffer.WriteAsync(chunk.AsMemory(0, bytesRead));
        }

        var bytes = buffer.ToArray();
        if (bytes.Length >= 3 && bytes[0] == 0xFF && bytes[1] == 0xD8 && bytes[2] == 0xFF)
        {
            return (bytes, "image/jpeg", "capture.jpg");
        }

        if (bytes.Length >= 8
            && bytes.AsSpan(0, 8).SequenceEqual(
                new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A }))
        {
            return (bytes, "image/png", "capture.png");
        }

        if (bytes.Length >= 12
            && bytes.AsSpan(0, 4).SequenceEqual("RIFF"u8)
            && bytes.AsSpan(8, 4).SequenceEqual("WEBP"u8))
        {
            return (bytes, "image/webp", "capture.webp");
        }

        throw new InvalidDataException("The photo must be JPEG, PNG, or WebP.");
    }
}
