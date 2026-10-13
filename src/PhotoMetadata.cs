using System.Buffers.Binary;
using System.IO;
using System.IO.Compression;
using System.Text;
using System.Xml;
using System.Xml.Linq;

namespace VrcPhotoPost;
public static class PhotoMetadata
{
    public const string WorldNamespace = "http://ns.vrchat.com/vrc/1.0/";
    const int MaxPacket = 1_048_576;
    public static string? ReadWorldName(string path)
    {
        try {
            using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            Span<byte> signature = stackalloc byte[8]; file.ReadExactly(signature);
            if (signature.SequenceEqual(new byte[] {137,80,78,71,13,10,26,10})) return ReadPng(file);
            if (signature[0] == 0xff && signature[1] == 0xd8) { file.Position = 2; return ReadJpeg(file); }
        } catch (Exception e) when (e is IOException or UnauthorizedAccessException or InvalidDataException or XmlException or DecoderFallbackException or ArgumentException) { }
        return null;
    }
    static string? ReadPng(Stream file)
    {
        Span<byte> header = stackalloc byte[8]; Span<byte> crc = stackalloc byte[4];
        for (int count = 0; count < 100_000 && file.Position < file.Length; count++) {
            file.ReadExactly(header);
            var length = BinaryPrimitives.ReadUInt32BigEndian(header);
            var type = Encoding.ASCII.GetString(header[4..]);
            if (length > file.Length - file.Position - 4) return null;
            if (type == "IEND") return null;
            if (type is not ("iTXt" or "tEXt" or "zTXt") || length > MaxPacket) { file.Seek(length + 4L, SeekOrigin.Current); continue; }
            var data = new byte[(int)length]; file.ReadExactly(data); file.ReadExactly(crc);
            if (Crc32(header[4..], data) != BinaryPrimitives.ReadUInt32BigEndian(crc)) continue;
            var end = Array.IndexOf(data, (byte)0);
            if (end < 0 || Encoding.ASCII.GetString(data, 0, end) != "XML:com.adobe.xmp") continue;
            byte[]? packet = null; int offset = end + 1;
            if (type == "tEXt") packet = data[offset..];
            else if (type == "zTXt") { if (offset < data.Length && data[offset++] == 0) packet = Inflate(data[offset..]); }
            else if (offset + 2 <= data.Length) {
                var compressed = data[offset++]; var method = data[offset++];
                if (compressed > 1 || method != 0) continue;
                for (int field = 0; field < 2; field++) { var zero = Array.IndexOf(data, (byte)0, offset); if (zero < 0) return null; offset = zero + 1; }
                packet = compressed == 1 ? Inflate(data[offset..]) : data[offset..];
            }
            if (packet is not null && ParseXmp(packet) is { } world) return world;
        }
        return null;
    }
    static string? ReadJpeg(Stream file)
    {
        var prefix = Encoding.ASCII.GetBytes("http://ns.adobe.com/xap/1.0/\0");
        Span<byte> size = stackalloc byte[2];
        while (file.Position < file.Length) {
            if (file.ReadByte() != 0xff) return null;
            int marker; do { marker = file.ReadByte(); } while (marker == 0xff);
            if (marker < 0 || marker is 0xda or 0xd9) return null;
            if (marker is 0x01 or >= 0xd0 and <= 0xd7) continue;
            file.ReadExactly(size); var length = BinaryPrimitives.ReadUInt16BigEndian(size) - 2;
            if (length < 0 || length > file.Length - file.Position) return null;
            if (marker != 0xe1) { file.Seek(length, SeekOrigin.Current); continue; }
            var data = new byte[length]; file.ReadExactly(data);
            if (data.AsSpan().StartsWith(prefix) && ParseXmp(data[prefix.Length..]) is { } world) return world;
        }
        return null;
    }
    static byte[] Inflate(byte[] bytes)
    {
        using var input = new MemoryStream(bytes); using var zlib = new ZLibStream(input, CompressionMode.Decompress); using var output = new MemoryStream();
        var buffer = new byte[4096]; int count;
        while ((count = zlib.Read(buffer)) > 0) { if (output.Length + count > MaxPacket) throw new InvalidDataException("XMP too large"); output.Write(buffer, 0, count); }
        return output.ToArray();
    }
    internal static string? ParseXmp(byte[] bytes)
    {
        if (bytes.Length > MaxPacket) return null;
        using var input = new MemoryStream(bytes);
        using var reader = XmlReader.Create(input, new XmlReaderSettings { DtdProcessing = DtdProcessing.Prohibit, XmlResolver = null, MaxCharactersInDocument = MaxPacket });
        var xml = XDocument.Load(reader);
        XName name = XNamespace.Get(WorldNamespace) + "WorldDisplayName";
        var values = xml.Descendants().SelectMany(e => e.Name == name ? new[] { e.Value } : e.Attributes(name).Select(a => a.Value));
        return values.Select(NormalizeName).FirstOrDefault(v => v is not null);
    }
    static string? NormalizeName(string value)
    {
        var normalized = string.Join(" ", value.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (normalized.Length is 0 or > 2048 || normalized.Any(char.IsControl)) return null;
        return normalized;
    }
    internal static uint Crc32(ReadOnlySpan<byte> type, ReadOnlySpan<byte> data)
    {
        uint crc = uint.MaxValue;
        foreach (var b in type) Update(b);
        foreach (var b in data) Update(b);
        return ~crc;
        void Update(byte b) { crc ^= b; for (int i = 0; i < 8; i++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320u : 0); }
    }
}

public sealed class DraftTextSession
{
    public bool Consumed { get; private set; }
    public void Protect() => Consumed = true;
    public bool TryConsume() { if (Consumed) return false; Consumed = true; return true; }
    public void BeginNewDraft() => Consumed = false;
    public static string[] Worlds(IEnumerable<string?> names) => names.Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n!).Distinct(StringComparer.Ordinal).ToArray();
    public static string Build(IEnumerable<string?> names) => string.Join("\n", Worlds(names).Append("#VRchat"));
}
