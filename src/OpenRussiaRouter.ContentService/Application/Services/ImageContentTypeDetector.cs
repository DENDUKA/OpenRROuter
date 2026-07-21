namespace OpenRussiaRouter.ContentService.Application.Services;

/// <summary>
/// Derives the content type from the file's magic bytes.
///
/// The client-supplied content type is not trusted: the image endpoint echoes the stored type
/// back to browsers, so accepting the caller's word would let an admin upload a text/html
/// payload labelled as a PNG and get it served from our origin.
/// </summary>
public static class ImageContentTypeDetector
{
    /// <summary>Returns the detected content type, or <c>null</c> when the bytes match no known raster format.</summary>
    public static string? Detect(ReadOnlySpan<byte> bytes)
    {
        if (StartsWith(bytes, [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A]))
        {
            return "image/png";
        }

        if (StartsWith(bytes, [0xFF, 0xD8, 0xFF]))
        {
            return "image/jpeg";
        }

        if (StartsWith(bytes, "GIF87a"u8) || StartsWith(bytes, "GIF89a"u8))
        {
            return "image/gif";
        }

        // RIFF....WEBP
        if (bytes.Length >= 12 && StartsWith(bytes, "RIFF"u8) && bytes[8..12].SequenceEqual("WEBP"u8))
        {
            return "image/webp";
        }

        return null;
    }

    private static bool StartsWith(ReadOnlySpan<byte> bytes, ReadOnlySpan<byte> prefix) =>
        bytes.Length >= prefix.Length && bytes[..prefix.Length].SequenceEqual(prefix);
}
