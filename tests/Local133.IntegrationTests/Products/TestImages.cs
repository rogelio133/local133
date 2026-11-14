using System.Buffers.Binary;
using System.Text;
using SkiaSharp;

namespace Local133.IntegrationTests.Products;

/// <summary>Imágenes generadas para las pruebas de fotos de productos.</summary>
internal static class TestImages
{
    /// <summary>GIF de 1×1 (formato que no se acepta).</summary>
    public static readonly byte[] Gif = Convert.FromBase64String("R0lGODlhAQABAIAAAAAAAP///yH5BAEAAAAALAAAAAABAAEAAAIBRAA7");

    /// <summary>Imagen de un color, codificada en el formato pedido.</summary>
    public static byte[] Solid(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg, SKColor? color = null)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(color ?? new SKColor(200, 30, 120));
        return Encode(bitmap, format);
    }

    /// <summary>Mitad izquierda roja y mitad derecha azul (para revisar la orientación).</summary>
    public static byte[] RedLeftBlueRight(int width, int height, SKEncodedImageFormat format = SKEncodedImageFormat.Jpeg)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        using (var canvas = new SKCanvas(bitmap))
        {
            using var red = new SKPaint { Color = SKColors.Red };
            using var blue = new SKPaint { Color = SKColors.Blue };
            canvas.DrawRect(0, 0, width / 2f, height, red);
            canvas.DrawRect(width / 2f, 0, width / 2f, height, blue);
        }

        return Encode(bitmap, format);
    }

    /// <summary>PNG completamente transparente.</summary>
    public static byte[] TransparentPng(int width, int height)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(width, height, SKColorType.Rgba8888, SKAlphaType.Premul));
        bitmap.Erase(SKColors.Transparent);
        return Encode(bitmap, SKEncodedImageFormat.Png);
    }

    /// <summary>
    /// Inserta tras el SOI un segmento APP1 con EXIF: orientación <paramref name="orientation"/> y una descripción
    /// (como hacen las cámaras; aquí sustituye a datos personales como el GPS).
    /// </summary>
    public static byte[] WithExif(byte[] jpeg, ushort orientation)
    {
        var description = Encoding.ASCII.GetBytes("Ubicacion secreta\0");
        using var tiff = new MemoryStream();
        void U16(int v) { Span<byte> b = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(b, (ushort)v); tiff.Write(b); }
        void U32(int v) { Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(b, (uint)v); tiff.Write(b); }

        tiff.Write("MM"u8);
        U16(42);
        U32(8); // IFD0
        U16(2); // entradas
        // 0x010E ImageDescription (ASCII), apunta después del IFD.
        U16(0x010E); U16(2); U32(description.Length); U32(8 + 2 + (2 * 12) + 4);
        // 0x0112 Orientation (SHORT)
        U16(0x0112); U16(3); U32(1); U16(orientation); U16(0);
        U32(0); // sin IFD siguiente
        tiff.Write(description);

        var payload = Encoding.ASCII.GetBytes("Exif\0\0").Concat(tiff.ToArray()).ToArray();
        var segment = new byte[4 + payload.Length];
        segment[0] = 0xFF;
        segment[1] = 0xE1;
        BinaryPrimitives.WriteUInt16BigEndian(segment.AsSpan(2), (ushort)(payload.Length + 2));
        payload.CopyTo(segment, 4);

        return [.. jpeg.Take(2), .. segment, .. jpeg.Skip(2)];
    }

    /// <summary>
    /// PNG que declara <paramref name="width"/>×<paramref name="height"/> en su encabezado pero no trae los píxeles: basta
    /// para probar que se rechaza sin decodificar.
    /// </summary>
    public static byte[] PngHeaderOnly(int width, int height)
    {
        var ihdr = new byte[13];
        BinaryPrimitives.WriteInt32BigEndian(ihdr, width);
        BinaryPrimitives.WriteInt32BigEndian(ihdr.AsSpan(4), height);
        ihdr[8] = 8; // bits por canal
        ihdr[9] = 6; // RGBA
        byte[] signature = [0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A];
        // Un IDAT vacío: el decodificador lee el encabezado hasta el primer IDAT.
        return [.. signature, .. Chunk("IHDR", ihdr), .. Chunk("IDAT", []), .. Chunk("IEND", [])];
    }

    public static bool HasExif(byte[] jpeg) => IndexOf(jpeg, Encoding.ASCII.GetBytes("Exif\0\0")) >= 0;

    public static bool Contains(byte[] data, string ascii) => IndexOf(data, Encoding.ASCII.GetBytes(ascii)) >= 0;

    public static SKBitmap Decode(byte[] data) =>
        SKBitmap.Decode(data) ?? throw new InvalidOperationException("No se pudo decodificar la imagen.");

    public static bool IsJpeg(byte[] data) => data.Length > 3 && data[0] == 0xFF && data[1] == 0xD8 && data[2] == 0xFF;

    private static byte[] Encode(SKBitmap bitmap, SKEncodedImageFormat format)
    {
        using var data = bitmap.Encode(format, 90);
        return data.ToArray();
    }

    private static byte[] Chunk(string type, byte[] data)
    {
        var typeBytes = Encoding.ASCII.GetBytes(type);
        var chunk = new byte[12 + data.Length];
        BinaryPrimitives.WriteInt32BigEndian(chunk, data.Length);
        typeBytes.CopyTo(chunk, 4);
        data.CopyTo(chunk, 8);
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(8 + data.Length), Crc32([.. typeBytes, .. data]));
        return chunk;
    }

    private static uint Crc32(byte[] data)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var b in data)
        {
            crc ^= b;
            for (var i = 0; i < 8; i++)
            {
                crc = (crc & 1) != 0 ? (crc >> 1) ^ 0xEDB88320u : crc >> 1;
            }
        }

        return ~crc;
    }

    private static int IndexOf(byte[] data, byte[] pattern)
    {
        for (var i = 0; i <= data.Length - pattern.Length; i++)
        {
            if (data.AsSpan(i, pattern.Length).SequenceEqual(pattern))
            {
                return i;
            }
        }

        return -1;
    }
}
