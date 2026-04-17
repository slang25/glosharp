using ZstdSharp;
using ZstdSharp.Unsafe;

namespace GloSharp.Core;

internal interface IZstdCodec
{
    byte[] Compress(ReadOnlySpan<byte> input, int level, int windowLog);
    byte[] Decompress(ReadOnlySpan<byte> input);
}

internal sealed class ZstdSharpCodec : IZstdCodec
{
    public static readonly ZstdSharpCodec Instance = new();

    public byte[] Compress(ReadOnlySpan<byte> input, int level, int windowLog)
    {
        using var compressor = new Compressor(level);
        compressor.SetParameter(ZSTD_cParameter.ZSTD_c_windowLog, windowLog);
        return compressor.Wrap(input).ToArray();
    }

    public byte[] Decompress(ReadOnlySpan<byte> input)
    {
        using var decompressor = new Decompressor();
        decompressor.SetParameter(ZSTD_dParameter.ZSTD_d_windowLogMax, 31);
        return decompressor.Unwrap(input).ToArray();
    }
}
