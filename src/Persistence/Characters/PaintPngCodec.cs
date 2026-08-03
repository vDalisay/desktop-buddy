using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using DesktopBuddy.Domain.Painting;

namespace DesktopBuddy.Persistence.Characters;

/// <summary>Minimal, deterministic non-interlaced RGBA8 PNG codec for trusted paint surfaces.</summary>
public static class PaintPngCodec
{
    private static ReadOnlySpan<byte> Signature => [137, 80, 78, 71, 13, 10, 26, 10];

    public static byte[] Encode(ReadOnlySpan<byte> rgba)
    {
        if (rgba.Length != PaintPolicy.SurfaceBytes)
            throw new ArgumentException("Paint pixels must be exactly 512x512 RGBA8.", nameof(rgba));

        using var output = new MemoryStream();
        output.Write(Signature);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr, PaintPolicy.SurfaceSize);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[4..], PaintPolicy.SurfaceSize);
        ihdr[8] = 8;
        ihdr[9] = 6;
        WriteChunk(output, "IHDR"u8, ihdr);

        using var raw = new MemoryStream(PaintPolicy.SurfaceBytes + PaintPolicy.SurfaceSize);
        int rowBytes = PaintPolicy.SurfaceSize * PaintPolicy.BytesPerPixel;
        for (int y = 0; y < PaintPolicy.SurfaceSize; y++)
        {
            raw.WriteByte(0);
            raw.Write(rgba.Slice(y * rowBytes, rowBytes));
        }
        raw.Position = 0;
        using var compressed = new MemoryStream();
        using (var zlib = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true))
            raw.CopyTo(zlib);
        byte[] idat = compressed.ToArray();
        if (idat.Length > PaintPolicy.MaximumEncodedPngBytes)
            throw new InvalidDataException("Encoded paint PNG exceeds the 2 MiB limit.");
        WriteChunk(output, "IDAT"u8, idat);
        WriteChunk(output, "IEND"u8, ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    public static byte[] Decode(ReadOnlySpan<byte> png)
    {
        if (png.Length == 0 || png.Length > PaintPolicy.MaximumEncodedPngBytes)
            throw new InvalidDataException("Paint PNG is empty or exceeds the 2 MiB limit.");
        if (png.Length < Signature.Length || !png[..8].SequenceEqual(Signature))
            throw new InvalidDataException("Paint file is not a PNG.");

        int offset = 8;
        bool sawHeader = false;
        bool sawEnd = false;
        using var compressed = new MemoryStream();
        while (offset < png.Length)
        {
            if (png.Length - offset < 12)
                throw new InvalidDataException("PNG chunk is truncated.");
            uint length = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, 4));
            offset += 4;
            if (length > int.MaxValue || png.Length - offset < 8 + (int)length)
                throw new InvalidDataException("PNG chunk length is invalid.");
            ReadOnlySpan<byte> type = png.Slice(offset, 4);
            offset += 4;
            ReadOnlySpan<byte> data = png.Slice(offset, (int)length);
            offset += (int)length;
            uint expected = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, 4));
            offset += 4;
            if (Crc32(type, data) != expected)
                throw new InvalidDataException("PNG chunk CRC is invalid.");

            if (type.SequenceEqual("IHDR"u8))
            {
                if (sawHeader || data.Length != 13)
                    throw new InvalidDataException("PNG header is invalid.");
                sawHeader = true;
                if (BinaryPrimitives.ReadUInt32BigEndian(data) != PaintPolicy.SurfaceSize ||
                    BinaryPrimitives.ReadUInt32BigEndian(data[4..]) != PaintPolicy.SurfaceSize ||
                    data[8] != 8 || data[9] != 6 || data[10] != 0 || data[11] != 0 || data[12] != 0)
                {
                    throw new InvalidDataException("Paint PNG must be 512x512 non-interlaced RGBA8.");
                }
            }
            else if (type.SequenceEqual("IDAT"u8))
            {
                if (!sawHeader || sawEnd)
                    throw new InvalidDataException("PNG chunk order is invalid.");
                compressed.Write(data);
            }
            else if (type.SequenceEqual("IEND"u8))
            {
                if (data.Length != 0)
                    throw new InvalidDataException("PNG end chunk is invalid.");
                sawEnd = true;
                break;
            }
            else if ((type[0] & 0x20) == 0)
            {
                throw new InvalidDataException("Unsupported critical PNG chunk.");
            }
        }

        if (!sawHeader || !sawEnd)
            throw new InvalidDataException("PNG is incomplete.");
        compressed.Position = 0;
        int rowBytes = PaintPolicy.SurfaceSize * PaintPolicy.BytesPerPixel;
        int expectedRaw = (rowBytes + 1) * PaintPolicy.SurfaceSize;
        byte[] filtered = new byte[expectedRaw];
        using (var zlib = new ZLibStream(compressed, CompressionMode.Decompress))
        {
            int total = 0;
            while (total < filtered.Length)
            {
                int read = zlib.Read(filtered, total, filtered.Length - total);
                if (read == 0) break;
                total += read;
            }
            if (total != filtered.Length || zlib.ReadByte() != -1)
                throw new InvalidDataException("PNG decoded byte count is invalid.");
        }

        byte[] rgba = new byte[PaintPolicy.SurfaceBytes];
        byte[] previous = new byte[rowBytes];
        byte[] current = new byte[rowBytes];
        int source = 0;
        for (int y = 0; y < PaintPolicy.SurfaceSize; y++)
        {
            byte filter = filtered[source++];
            Buffer.BlockCopy(filtered, source, current, 0, rowBytes);
            source += rowBytes;
            Unfilter(current, previous, filter);
            Buffer.BlockCopy(current, 0, rgba, y * rowBytes, rowBytes);
            (previous, current) = (current, previous);
        }
        return rgba;
    }

    private static void Unfilter(Span<byte> row, ReadOnlySpan<byte> previous, byte filter)
    {
        for (int i = 0; i < row.Length; i++)
        {
            int left = i >= 4 ? row[i - 4] : 0;
            int up = previous[i];
            int upperLeft = i >= 4 ? previous[i - 4] : 0;
            row[i] = filter switch
            {
                0 => row[i],
                1 => unchecked((byte)(row[i] + left)),
                2 => unchecked((byte)(row[i] + up)),
                3 => unchecked((byte)(row[i] + ((left + up) / 2))),
                4 => unchecked((byte)(row[i] + Paeth(left, up, upperLeft))),
                _ => throw new InvalidDataException("Unsupported PNG filter."),
            };
        }
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream stream, ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length);
        stream.Write(length);
        stream.Write(type);
        stream.Write(data);
        BinaryPrimitives.WriteUInt32BigEndian(length, Crc32(type, data));
        stream.Write(length);
    }

    private static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = 0xFFFFFFFF;
        foreach (byte value in type) crc = Update(crc, value);
        foreach (byte value in data) crc = Update(crc, value);
        return ~crc;
    }

    private static uint Update(uint crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++)
            crc = (crc >> 1) ^ (0xEDB88320u & (uint)-(int)(crc & 1));
        return crc;
    }
}
