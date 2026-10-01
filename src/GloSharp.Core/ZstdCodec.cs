using ZstdSharp;
using ZstdSharp.Unsafe;

namespace GloSharp.Core;

internal interface IZstdCodec
{
    byte[] Compress(ReadOnlySpan<byte> input, int level, int windowLog);
    byte[] Decompress(ReadOnlySpan<byte> input, int maxDecompressedSize = ZstdSharpCodec.DefaultMaxDecompressedSize);
}

internal sealed class ZstdSharpCodec : IZstdCodec
{
    /// <summary>
    /// Upper bound on a decompressed payload (1 GiB). Real .glocontext payloads are a few MB;
    /// the cap stops a tiny malicious file from demanding gigabytes of memory.
    /// </summary>
    public const int DefaultMaxDecompressedSize = 1 << 30;

    public static readonly ZstdSharpCodec Instance = new();

    public byte[] Compress(ReadOnlySpan<byte> input, int level, int windowLog)
    {
        using var compressor = new Compressor(level);
        compressor.SetParameter(ZSTD_cParameter.ZSTD_c_windowLog, windowLog);
        return compressor.Wrap(input).ToArray();
    }

    /// <summary>
    /// Decompresses a single zstd frame whose header records its content size (as
    /// <see cref="Compress"/> writes). Throws <see cref="InvalidDataException"/> for corrupt or
    /// truncated input and for frames larger than <paramref name="maxDecompressedSize"/>.
    /// </summary>
    public byte[] Decompress(ReadOnlySpan<byte> input, int maxDecompressedSize = DefaultMaxDecompressedSize)
    {
        ulong declared;
        try
        {
            declared = Decompressor.GetDecompressedSize(input);
        }
        catch (Exception ex) when (ex is ZstdException or ArgumentException)
        {
            throw new InvalidDataException(
                $"Compressed payload is truncated or corrupt (zstd: {ex.Message}).", ex);
        }

        if (declared > (ulong)maxDecompressedSize)
            throw new InvalidDataException(
                $"Compressed payload declares {declared:N0} bytes when decompressed, more than the " +
                $"{maxDecompressedSize:N0}-byte limit; the file is corrupt or not a glosharp artifact.");

        try
        {
            using var decompressor = new Decompressor();
            decompressor.SetParameter(ZSTD_dParameter.ZSTD_d_windowLogMax, 31);
            return decompressor.Unwrap(input, maxDecompressedSize).ToArray();
        }
        catch (Exception ex) when (ex is ZstdException or ArgumentException)
        {
            throw new InvalidDataException(
                $"Compressed payload is truncated or corrupt (zstd: {ex.Message}).", ex);
        }
    }
}
