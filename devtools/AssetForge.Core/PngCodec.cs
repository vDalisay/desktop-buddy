using System.Buffers.Binary;
using System.IO.Compression;

namespace DesktopBuddy.AssetForge.Core;

public sealed class RgbaImage
{
    public RgbaImage(int width, int height, byte[] pixels)
    {
        if (width <= 0 || height <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (pixels.Length != checked(width * height * 4)) throw new ArgumentException("RGBA byte count does not match dimensions.", nameof(pixels));
        Width = width;
        Height = height;
        Pixels = pixels;
    }
    public int Width { get; }
    public int Height { get; }
    public byte[] Pixels { get; }
    public byte Alpha(int x, int y) => Pixels[((y * Width) + x) * 4 + 3];
}

public static class PngCodec
{
    private static readonly byte[] Signature = [137, 80, 78, 71, 13, 10, 26, 10];

    public static RgbaImage DecodeRgba8(ReadOnlySpan<byte> png)
    {
        if (png.Length < 33 || !png[..8].SequenceEqual(Signature)) throw new FormatException("Invalid PNG signature.");
        int offset = 8, width = 0, height = 0;
        var idat = new MemoryStream();
        bool sawHeader = false, sawEnd = false;
        while (offset + 12 <= png.Length)
        {
            uint length = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, 4));
            offset += 4;
            if (length > int.MaxValue || offset + 4 + (int)length + 4 > png.Length) throw new FormatException("Invalid PNG chunk length.");
            ReadOnlySpan<byte> type = png.Slice(offset, 4); offset += 4;
            ReadOnlySpan<byte> data = png.Slice(offset, (int)length); offset += (int)length;
            uint expectedCrc = BinaryPrimitives.ReadUInt32BigEndian(png.Slice(offset, 4)); offset += 4;
            if (Crc32(type, data) != expectedCrc) throw new FormatException("PNG chunk CRC mismatch.");
            string kind = System.Text.Encoding.ASCII.GetString(type);
            if (kind == "IHDR")
            {
                if (sawHeader || data.Length != 13) throw new FormatException("Invalid PNG IHDR.");
                width = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data[..4]));
                height = checked((int)BinaryPrimitives.ReadUInt32BigEndian(data.Slice(4, 4)));
                if (width <= 0 || height <= 0 || data[8] != 8 || data[9] != 6 || data[10] != 0 || data[11] != 0 || data[12] != 0)
                    throw new FormatException("Asset Forge requires non-interlaced 8-bit RGBA PNG (color type 6).");
                sawHeader = true;
            }
            else if (kind == "IDAT") idat.Write(data);
            else if (kind == "IEND") { sawEnd = true; break; }
        }
        if (!sawHeader || !sawEnd || idat.Length == 0) throw new FormatException("PNG is missing required chunks.");
        int stride = checked(width * 4);
        byte[] filtered = new byte[checked((stride + 1) * height)];
        idat.Position = 0;
        using (var z = new ZLibStream(idat, CompressionMode.Decompress, leaveOpen: true))
        {
            int read = 0;
            while (read < filtered.Length)
            {
                int n = z.Read(filtered, read, filtered.Length - read);
                if (n == 0) break;
                read += n;
            }
            if (read != filtered.Length || z.ReadByte() != -1) throw new FormatException("PNG decompressed payload has an unexpected size.");
        }
        byte[] pixels = new byte[checked(stride * height)];
        int source = 0;
        for (int y = 0; y < height; y++)
        {
            int filter = filtered[source++];
            int row = y * stride;
            for (int x = 0; x < stride; x++)
            {
                byte raw = filtered[source++];
                int left = x >= 4 ? pixels[row + x - 4] : 0;
                int up = y > 0 ? pixels[row - stride + x] : 0;
                int upLeft = y > 0 && x >= 4 ? pixels[row - stride + x - 4] : 0;
                int value = filter switch
                {
                    0 => raw,
                    1 => raw + left,
                    2 => raw + up,
                    3 => raw + ((left + up) >> 1),
                    4 => raw + Paeth(left, up, upLeft),
                    _ => throw new FormatException($"Unsupported PNG filter {filter}."),
                };
                pixels[row + x] = unchecked((byte)value);
            }
        }
        return new RgbaImage(width, height, pixels);
    }

    public static byte[] EncodeRgba8(RgbaImage image)
    {
        using var output = new MemoryStream();
        output.Write(Signature);
        Span<byte> ihdr = stackalloc byte[13];
        BinaryPrimitives.WriteUInt32BigEndian(ihdr[..4], (uint)image.Width);
        BinaryPrimitives.WriteUInt32BigEndian(ihdr.Slice(4, 4), (uint)image.Height);
        ihdr[8] = 8; ihdr[9] = 6; ihdr[10] = 0; ihdr[11] = 0; ihdr[12] = 0;
        WriteChunk(output, "IHDR", ihdr);
        using var raw = new MemoryStream();
        int stride = image.Width * 4;
        for (int y = 0; y < image.Height; y++)
        {
            raw.WriteByte(0);
            raw.Write(image.Pixels, y * stride, stride);
        }
        raw.Position = 0;
        using var compressed = new MemoryStream();
        using (var z = new ZLibStream(compressed, CompressionLevel.SmallestSize, leaveOpen: true)) raw.CopyTo(z);
        WriteChunk(output, "IDAT", compressed.ToArray());
        WriteChunk(output, "IEND", ReadOnlySpan<byte>.Empty);
        return output.ToArray();
    }

    public static RgbaImage ResizeBox(RgbaImage source, int target)
    {
        if (target <= 0 || source.Width % target != 0 || source.Height % target != 0) throw new ArgumentException("Target resolution must divide source dimensions.");
        int blockX = source.Width / target, blockY = source.Height / target;
        byte[] pixels = new byte[target * target * 4];
        for (int ty = 0; ty < target; ty++)
        for (int tx = 0; tx < target; tx++)
        {
            long r = 0, g = 0, b = 0, a = 0;
            int count = blockX * blockY;
            for (int y = 0; y < blockY; y++)
            for (int x = 0; x < blockX; x++)
            {
                int si = ((((ty * blockY) + y) * source.Width) + (tx * blockX) + x) * 4;
                r += source.Pixels[si]; g += source.Pixels[si + 1]; b += source.Pixels[si + 2]; a += source.Pixels[si + 3];
            }
            int di = ((ty * target) + tx) * 4;
            pixels[di] = (byte)((r + count / 2) / count);
            pixels[di + 1] = (byte)((g + count / 2) / count);
            pixels[di + 2] = (byte)((b + count / 2) / count);
            pixels[di + 3] = (byte)((a + count / 2) / count);
        }
        return new RgbaImage(target, target, pixels);
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c, pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void WriteChunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> length = stackalloc byte[4];
        BinaryPrimitives.WriteUInt32BigEndian(length, (uint)data.Length); output.Write(length);
        byte[] typeBytes = System.Text.Encoding.ASCII.GetBytes(type); output.Write(typeBytes); output.Write(data);
        Span<byte> crc = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(crc, Crc32(typeBytes, data)); output.Write(crc);
    }

    private static uint Crc32(ReadOnlySpan<byte> a, ReadOnlySpan<byte> b)
    {
        uint crc = 0xffffffffu;
        foreach (byte value in a) crc = Update(crc, value);
        foreach (byte value in b) crc = Update(crc, value);
        return ~crc;
    }

    private static uint Update(uint crc, byte value)
    {
        crc ^= value;
        for (int i = 0; i < 8; i++) crc = (crc & 1) != 0 ? 0xedb88320u ^ (crc >> 1) : crc >> 1;
        return crc;
    }
}
