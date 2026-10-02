using System.Buffers.Binary;

namespace Compositor.Imaging;

internal static class PngIntegrity
{
    private static readonly uint[] CrcTable = CreateCrcTable();

    public static void ValidateIfPng(Stream stream)
    {
        Span<byte> header = stackalloc byte[8];
        Span<byte> checksum = stackalloc byte[4];
        try
        {
            if (stream.Length < 8) return;
            stream.ReadExactly(header);
            if (!header.SequenceEqual(new byte[] { 137, 80, 78, 71, 13, 10, 26, 10 })) return;
            var buffer = new byte[64 * 1024];
            bool first = true, seenData = false, endedData = false, seenPalette = false;
            while (stream.Position < stream.Length)
            {
                if (stream.Length - stream.Position < 12)
                    throw new InvalidDataException("Truncated PNG chunk.");
                stream.ReadExactly(header);
                uint length = BinaryPrimitives.ReadUInt32BigEndian(header);
                uint type = BinaryPrimitives.ReadUInt32BigEndian(header[4..]);
                if (length > int.MaxValue || (long)length + 4 > stream.Length - stream.Position)
                    throw new InvalidDataException("Invalid PNG chunk length.");
                foreach (byte value in header[4..])
                    if (value is not (>= (byte)'A' and <= (byte)'Z') and not (>= (byte)'a' and <= (byte)'z'))
                        throw new InvalidDataException("Invalid PNG chunk name.");
                if (first && (type != 0x49484452 || length != 13))
                    throw new InvalidDataException("PNG must start with IHDR.");
                switch (type)
                {
                    case 0x49484452: // IHDR
                        if (!first) throw new InvalidDataException("Repeated PNG header.");
                        break;
                    case 0x504c5445: // PLTE
                        if (seenPalette || seenData || length == 0 || length > 768 || length % 3 != 0)
                            throw new InvalidDataException("Invalid PNG palette.");
                        seenPalette = true;
                        break;
                    case 0x49444154: // IDAT
                        if (endedData) throw new InvalidDataException("PNG image chunks must be consecutive.");
                        seenData = true;
                        break;
                    case 0x49454e44: // IEND
                        if (length != 0 || !seenData) throw new InvalidDataException("Invalid PNG end chunk.");
                        break;
                    default:
                        if ((header[4] & 32) == 0)
                            throw new InvalidDataException("Unsupported critical PNG chunk.");
                        break;
                }
                first = false;
                if (seenData && type != 0x49444154) endedData = true;
                uint crc = UpdateCrc(uint.MaxValue, header[4..]);
                uint remaining = length;
                while (remaining > 0)
                {
                    int count = (int)Math.Min(remaining, (uint)buffer.Length);
                    stream.ReadExactly(buffer.AsSpan(0, count));
                    crc = UpdateCrc(crc, buffer.AsSpan(0, count));
                    remaining -= (uint)count;
                }
                stream.ReadExactly(checksum);
                if ((crc ^ uint.MaxValue) != BinaryPrimitives.ReadUInt32BigEndian(checksum))
                    throw new InvalidDataException("PNG chunk checksum mismatch.");
                if (type == 0x49454e44)
                {
                    if (stream.Position != stream.Length)
                        throw new InvalidDataException("Unexpected data after PNG end.");
                    return;
                }
            }
            throw new InvalidDataException("Missing PNG end chunk.");
        }
        finally { stream.Position = 0; }
    }

    private static uint UpdateCrc(uint crc, ReadOnlySpan<byte> bytes)
    {
        foreach (byte value in bytes) crc = CrcTable[(crc ^ value) & 255] ^ (crc >> 8);
        return crc;
    }

    private static uint[] CreateCrcTable()
    {
        var table = new uint[256];
        for (uint value = 0; value < table.Length; value++)
        {
            uint crc = value;
            for (int bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0);
            table[value] = crc;
        }
        return table;
    }
}
