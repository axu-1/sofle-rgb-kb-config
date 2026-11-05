using System.Buffers.Binary;

namespace Sofle.Status;

public enum KeyboardOutput : byte
{
    None = 0,
    Usb = 1,
    Bluetooth = 2
}

public sealed record KeyboardStatus(
    byte Layer,
    byte? LeftBattery,
    byte? RightBattery,
    byte Profile,
    KeyboardOutput Output,
    bool HostConnected,
    bool UsbPowered,
    bool RightConnected,
    bool HasLockIndicators,
    byte LockIndicators,
    uint ActiveLayers,
    uint Sequence,
    uint? RightSampleAgeSeconds)
{
    public const int PacketSize = 20;
    public static readonly Guid ServiceUuid = new("8c6b7a10-5f2d-4a7c-9b31-6e8d0f2c4100");
    public static readonly Guid CharacteristicUuid = new("8c6b7a10-5f2d-4a7c-9b31-6e8d0f2c4101");

    public static KeyboardStatus Parse(ReadOnlySpan<byte> packet)
    {
        if (packet.Length != PacketSize)
            throw new FormatException($"Expected {PacketSize} status bytes; received {packet.Length}.");
        if (packet[0] != 1)
            throw new FormatException($"Unsupported status protocol {packet[0]}; update viewer and firmware together.");
        if (packet[1] >= 32 || packet[4] >= 16 || packet[5] > 2 || (packet[6] & 0xf0) != 0)
            throw new FormatException("Invalid layer, profile, output or flags in keyboard status.");

        var age = BinaryPrimitives.ReadUInt32LittleEndian(packet[16..]);
        return new(
            packet[1], Battery(packet[2]), Battery(packet[3]), packet[4], (KeyboardOutput)packet[5],
            (packet[6] & 1) != 0, (packet[6] & 2) != 0, (packet[6] & 4) != 0, (packet[6] & 8) != 0,
            packet[7], BinaryPrimitives.ReadUInt32LittleEndian(packet[8..]),
            BinaryPrimitives.ReadUInt32LittleEndian(packet[12..]), age == uint.MaxValue ? null : age);
    }

    private static byte? Battery(byte value) => value switch
    {
        <= 100 => value,
        255 => null,
        _ => throw new FormatException($"Invalid battery percentage {value}.")
    };
}
