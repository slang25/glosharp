using System.Buffers.Binary;
using System.Text;

namespace GloSharp.Core;

internal static class GloContextFormat
{
    public const int HeaderSize = 28;
    public const byte CurrentVersion = 0x01;

    public static ReadOnlySpan<byte> Magic => "GLOCTX"u8;

    public static void WriteHeader(Span<byte> buffer)
    {
        if (buffer.Length < HeaderSize)
            throw new ArgumentException($"Header buffer must be at least {HeaderSize} bytes", nameof(buffer));

        Magic.CopyTo(buffer);
        buffer[6] = CurrentVersion;
        buffer[7] = 0;
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(8, 8), 0);
        BinaryPrimitives.WriteUInt32LittleEndian(buffer.Slice(16, 4), 0);
        BinaryPrimitives.WriteUInt64LittleEndian(buffer.Slice(20, 8), 0);
    }

    public static GloContextHeader ReadHeader(ReadOnlySpan<byte> buffer)
    {
        if (buffer.Length < HeaderSize)
            throw new InvalidDataException($"File is too small to contain a {nameof(GloContextFormat)} header");

        if (!buffer.Slice(0, 6).SequenceEqual(Magic))
            throw new InvalidDataException(
                "File is not a .glocontext (missing GLOCTX magic bytes). Expected either a .glocontext (GLOCTX magic) or a .complog (zip archive).");

        var version = buffer[6];
        if (version != CurrentVersion)
            throw new InvalidDataException(
                $"Unsupported .glocontext format version: 0x{version:X2}. This build supports version 0x{CurrentVersion:X2}.");

        var flags = buffer[7];
        if (flags != 0)
            throw new InvalidDataException(
                $"Unrecognized .glocontext flags byte: 0x{flags:X2}. This build only understands flag value 0x00.");

        var baselineId = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(8, 8));
        var baselineVersion = BinaryPrimitives.ReadUInt32LittleEndian(buffer.Slice(16, 4));
        var reserved = BinaryPrimitives.ReadUInt64LittleEndian(buffer.Slice(20, 8));

        if (baselineId != 0 || baselineVersion != 0)
            throw new InvalidDataException(
                "This .glocontext was written against a baseline, which is not supported by this reader. " +
                "Baseline support is reserved for a future format version.");

        if (reserved != 0)
            throw new InvalidDataException(
                "Reserved header bytes are non-zero. This build may be too old to read this .glocontext.");

        return new GloContextHeader(version, flags, baselineId, baselineVersion);
    }

    public static bool LooksLikeGloContext(ReadOnlySpan<byte> buffer)
    {
        return buffer.Length >= Magic.Length && buffer.Slice(0, Magic.Length).SequenceEqual(Magic);
    }

    public static bool LooksLikeZip(ReadOnlySpan<byte> buffer)
    {
        return buffer.Length >= 4
            && buffer[0] == 0x50 && buffer[1] == 0x4B && buffer[2] == 0x03 && buffer[3] == 0x04;
    }
}

internal readonly record struct GloContextHeader(
    byte Version,
    byte Flags,
    ulong BaselineId,
    uint BaselineVersion);
