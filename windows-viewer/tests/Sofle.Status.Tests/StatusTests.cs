using System.Buffers.Binary;
using Xunit;

namespace Sofle.Status.Tests;

public sealed class StatusTests
{
    private static byte[] Packet()
    {
        byte[] packet = [1, 3, 87, 92, 0, 2, 13, 2, 15, 0, 0, 0, 42, 0, 0, 0, 5, 0, 0, 0];
        return packet;
    }

    [Fact]
    public void ParsesActualLayerBatteriesProfileAndFlags()
    {
        var status = KeyboardStatus.Parse(Packet());
        Assert.Equal(3, status.Layer);
        Assert.Equal<byte?>(87, status.LeftBattery);
        Assert.Equal<byte?>(92, status.RightBattery);
        Assert.Equal(0, status.Profile);
        Assert.Equal(KeyboardOutput.Bluetooth, status.Output);
        Assert.True(status.HostConnected);
        Assert.False(status.UsbPowered);
        Assert.True(status.RightConnected);
        Assert.True(status.HasLockIndicators);
        Assert.Equal(2, status.LockIndicators);
        Assert.Equal(15u, status.ActiveLayers);
        Assert.Equal(42u, status.Sequence);
        Assert.Equal(5u, status.RightSampleAgeSeconds);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(19)]
    [InlineData(21)]
    public void RejectsIncorrectPacketLength(int length) =>
        Assert.Throws<FormatException>(() => KeyboardStatus.Parse(new byte[length]));

    [Theory]
    [InlineData(0, 2)]
    [InlineData(1, 32)]
    [InlineData(2, 101)]
    [InlineData(3, 254)]
    [InlineData(4, 16)]
    [InlineData(5, 3)]
    [InlineData(6, 16)]
    public void RejectsMalformedFields(int offset, byte value)
    {
        var packet = Packet();
        packet[offset] = value;
        Assert.Throws<FormatException>(() => KeyboardStatus.Parse(packet));
    }

    [Fact]
    public void UnknownBatteryAndSampleDoNotBecomeZero()
    {
        var packet = Packet();
        packet[2] = packet[3] = 255;
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(16), uint.MaxValue);
        var status = KeyboardStatus.Parse(packet);
        Assert.Null(status.LeftBattery);
        Assert.Null(status.RightBattery);
        Assert.Null(status.RightSampleAgeSeconds);
        Assert.Contains("L ?  R ?", StatusText.Details(status, TimeSpan.Zero));
    }

    [Fact]
    public void ZeroBatteryIsRealAndDisconnectedRightIsOffline()
    {
        var status = KeyboardStatus.Parse(Packet()) with { LeftBattery = 0, RightConnected = false };
        Assert.Contains("L 0%  R offline", StatusText.Details(status, TimeSpan.Zero));
    }

    [Fact]
    public void OldUnchangedBatterySampleDoesNotMeanDisconnected()
    {
        var status = KeyboardStatus.Parse(Packet()) with { RightSampleAgeSeconds = 3600 };
        Assert.Contains("R 92%", StatusText.Details(status, TimeSpan.FromHours(1)));
        Assert.Contains("7200s ago", StatusText.Tooltip(status, new Dictionary<int, string>(), TimeSpan.FromHours(1)));
    }

    [Fact]
    public void OutputProfileAndCapsMatchFirmware()
    {
        var status = KeyboardStatus.Parse(Packet()) with { Output = KeyboardOutput.Usb, UsbPowered = true, Profile = 4 };
        Assert.Contains("L 87% +USB  R 92%  USB P5  CAPS", StatusText.Details(status, TimeSpan.Zero));
    }

    [Fact]
    public void UnnamedLayerUsesNumberAndMappedLayerUsesName()
    {
        var names = new Dictionary<int, string> { [4] = "GAMING" };
        Assert.Equal("GAMING", StatusText.LayerName(4, names));
        Assert.Equal("Layer 5", StatusText.LayerName(5, names));
    }

    [Fact]
    public void ReadsLittleEndianMaskAndSequenceWithoutSignedOverflow()
    {
        var packet = Packet();
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(8), 0x80000001);
        BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(12), uint.MaxValue);
        var status = KeyboardStatus.Parse(packet);
        Assert.Equal(0x80000001u, status.ActiveLayers);
        Assert.Equal(uint.MaxValue, status.Sequence);
    }
}
