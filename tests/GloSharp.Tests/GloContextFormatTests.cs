using GloSharp.Core;

namespace GloSharp.Tests;

public class GloContextFormatTests
{
    [Test]
    public async Task WriteHeader_ThenReadHeader_RoundTrips()
    {
        var buffer = new byte[GloContextFormat.HeaderSize];
        GloContextFormat.WriteHeader(buffer);
        var header = GloContextFormat.ReadHeader(buffer);

        await Assert.That(header.Version).IsEqualTo(GloContextFormat.CurrentVersion);
        await Assert.That(header.Flags).IsEqualTo((byte)0);
        await Assert.That(header.BaselineId).IsEqualTo((ulong)0);
        await Assert.That(header.BaselineVersion).IsEqualTo((uint)0);
    }

    [Test]
    public void ReadHeader_BadMagic_Throws()
    {
        var buffer = new byte[GloContextFormat.HeaderSize];
        buffer[0] = (byte)'N'; buffer[1] = (byte)'O'; buffer[2] = (byte)'P';
        buffer[3] = (byte)'E'; buffer[4] = 0; buffer[5] = 0;

        Assert.Throws<InvalidDataException>(() => GloContextFormat.ReadHeader(buffer));
    }

    [Test]
    public void ReadHeader_UnknownVersion_Throws()
    {
        var buffer = new byte[GloContextFormat.HeaderSize];
        GloContextFormat.WriteHeader(buffer);
        buffer[6] = 0xFF;

        Assert.Throws<InvalidDataException>(() => GloContextFormat.ReadHeader(buffer));
    }

    [Test]
    public void ReadHeader_UnknownFlags_Throws()
    {
        var buffer = new byte[GloContextFormat.HeaderSize];
        GloContextFormat.WriteHeader(buffer);
        buffer[7] = 0x01;

        Assert.Throws<InvalidDataException>(() => GloContextFormat.ReadHeader(buffer));
    }

    [Test]
    public void ReadHeader_NonZeroBaselineId_Throws()
    {
        var buffer = new byte[GloContextFormat.HeaderSize];
        GloContextFormat.WriteHeader(buffer);
        buffer[8] = 0x42;

        Assert.Throws<InvalidDataException>(() => GloContextFormat.ReadHeader(buffer));
    }

    [Test]
    public void ReadHeader_NonZeroBaselineVersion_Throws()
    {
        var buffer = new byte[GloContextFormat.HeaderSize];
        GloContextFormat.WriteHeader(buffer);
        buffer[16] = 0x01;

        Assert.Throws<InvalidDataException>(() => GloContextFormat.ReadHeader(buffer));
    }

    [Test]
    public void ReadHeader_NonZeroReserved_Throws()
    {
        var buffer = new byte[GloContextFormat.HeaderSize];
        GloContextFormat.WriteHeader(buffer);
        buffer[20] = 0x01;

        Assert.Throws<InvalidDataException>(() => GloContextFormat.ReadHeader(buffer));
    }

    [Test]
    public void ReadHeader_TooSmall_Throws()
    {
        var buffer = new byte[4];
        Assert.Throws<InvalidDataException>(() => GloContextFormat.ReadHeader(buffer));
    }

    [Test]
    public async Task LooksLikeGloContext_WithMagic_ReturnsTrue()
    {
        var buffer = new byte[GloContextFormat.HeaderSize];
        GloContextFormat.WriteHeader(buffer);
        await Assert.That(GloContextFormat.LooksLikeGloContext(buffer)).IsTrue();
    }

    [Test]
    public async Task LooksLikeGloContext_WithoutMagic_ReturnsFalse()
    {
        var buffer = new byte[] { 0x50, 0x4B, 0x03, 0x04 };
        await Assert.That(GloContextFormat.LooksLikeGloContext(buffer)).IsFalse();
    }

    [Test]
    public async Task LooksLikeZip_WithZipMagic_ReturnsTrue()
    {
        var buffer = new byte[] { 0x50, 0x4B, 0x03, 0x04, 0x00, 0x00 };
        await Assert.That(GloContextFormat.LooksLikeZip(buffer)).IsTrue();
    }

    [Test]
    public async Task LooksLikeZip_WithoutZipMagic_ReturnsFalse()
    {
        var buffer = new byte[GloContextFormat.HeaderSize];
        GloContextFormat.WriteHeader(buffer);
        await Assert.That(GloContextFormat.LooksLikeZip(buffer)).IsFalse();
    }
}
