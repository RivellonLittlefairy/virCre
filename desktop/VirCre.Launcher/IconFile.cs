using System.Buffers.Binary;

namespace VirCre.Launcher;

public static class IconFile {
    public static void ValidatePng(byte[] png) {
        ReadOnlySpan<byte> signature = [137,80,78,71,13,10,26,10];
        if (png.Length is < 33 or > 131072 || !png.AsSpan(0,8).SequenceEqual(signature) || !png.AsSpan(12,4).SequenceEqual("IHDR"u8)) throw new InvalidDataException("图标不是有效的 PNG。");
        var width = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16,4));
        var height = BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20,4));
        if (width is 0 or > 256 || height is 0 or > 256) throw new InvalidDataException("图标尺寸无效。");
    }
    public static byte[] FromPng(byte[] png) {
        ValidatePng(png);
        var icon = new byte[22 + png.Length];
        icon[2] = 1;icon[4] = 1;
        icon[6] = (byte)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(16,4));
        icon[7] = (byte)BinaryPrimitives.ReadUInt32BigEndian(png.AsSpan(20,4));
        icon[10] = 1;icon[12] = 32;
        BinaryPrimitives.WriteInt32LittleEndian(icon.AsSpan(14,4), png.Length);
        BinaryPrimitives.WriteInt32LittleEndian(icon.AsSpan(18,4), 22);
        png.CopyTo(icon,22);return icon;
    }
}
