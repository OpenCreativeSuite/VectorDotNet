using System.Text;

namespace VectorDotNet.Core;

/// <summary>One face of a font family found on the machine.</summary>
public sealed record FontFaceInfo(string Path, int Weight, bool Italic);

/// <summary>
/// Discovers font families by reading the name tables of installed font files.
/// The scan is pure .NET and works on Windows, macOS and Linux; no platform
/// font API is used. Results are cached for the session.
/// </summary>
public static class SystemFontCatalog
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, List<FontFaceInfo>> Families = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, string> DisplayNames = new(StringComparer.OrdinalIgnoreCase);
    private static IReadOnlyList<string> _names = [];
    private static bool _loaded;

    private static readonly string[] FallbackFamilies =
    [
        "Segoe UI", "Arial", "Helvetica", "Georgia", "Verdana", "Tahoma",
        "Times New Roman", "Courier New", "Calibri", "Inter", "Roboto",
        "DejaVu Sans", "Liberation Sans", "Noto Sans", "Open Sans", "Lato",
    ];

    /// <summary>Families in alphabetical order.</summary>
    public static IReadOnlyList<string> FamilyNames
    {
        get
        {
            EnsureLoaded();
            return _names;
        }
    }

    /// <summary>Faces recorded for a family.</summary>
    public static IReadOnlyList<FontFaceInfo> FacesFor(string family)
    {
        EnsureLoaded();
        lock (Gate)
        {
            return Families.TryGetValue(family, out List<FontFaceInfo>? faces)
                ? faces.ToList()
                : [];
        }
    }

    /// <summary>Adds a family from a font file outside the standard folders.</summary>
    public static void AddExternal(string family, string path, int weight, bool italic)
    {
        if (string.IsNullOrWhiteSpace(family) || string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        lock (Gate)
        {
            if (!Families.TryGetValue(family, out List<FontFaceInfo>? faces))
            {
                faces = [];
                Families[family] = faces;
                DisplayNames[family] = family;
            }

            if (!faces.Any(face => string.Equals(face.Path, path, StringComparison.OrdinalIgnoreCase) && face.Weight == weight && face.Italic == italic))
            {
                faces.Add(new FontFaceInfo(path, weight, italic));
            }

            RebuildNames();
        }
    }

    /// <summary>Reads the family name from a font file, or null when unreadable.</summary>
    public static string? ReadFamilyName(string path)
    {
        try
        {
            using FileStream stream = File.OpenRead(path);
            List<FontFace> faces = ReadFaces(stream);
            return faces.FirstOrDefault()?.Family;
        }
        catch
        {
            return null;
        }
    }

    private static void EnsureLoaded()
    {
        lock (Gate)
        {
            if (_loaded)
            {
                return;
            }

            _loaded = true;
            foreach (string directory in FontDirectories())
            {
                try
                {
                    if (!Directory.Exists(directory))
                    {
                        continue;
                    }

                    foreach (string file in Directory.EnumerateFiles(directory, "*", SearchOption.AllDirectories))
                    {
                        string extension = Path.GetExtension(file);
                        if (!extension.Equals(".ttf", StringComparison.OrdinalIgnoreCase)
                            && !extension.Equals(".otf", StringComparison.OrdinalIgnoreCase)
                            && !extension.Equals(".ttc", StringComparison.OrdinalIgnoreCase)
                            && !extension.Equals(".otc", StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        try
                        {
                            using FileStream stream = File.OpenRead(file);
                            foreach (FontFace face in ReadFaces(stream))
                            {
                                if (string.IsNullOrWhiteSpace(face.Family))
                                {
                                    continue;
                                }

                                if (!Families.TryGetValue(face.Family, out List<FontFaceInfo>? faces))
                                {
                                    faces = [];
                                    Families[face.Family] = faces;
                                    DisplayNames[face.Family] = face.Family;
                                }

                                if (!faces.Any(existing => string.Equals(existing.Path, file, StringComparison.OrdinalIgnoreCase)
                                    && existing.Weight == face.Weight
                                    && existing.Italic == face.Italic))
                                {
                                    faces.Add(new FontFaceInfo(file, face.Weight, face.Italic));
                                }
                            }
                        }
                        catch
                        {
                            // A malformed or unreadable font must not stop discovery.
                        }
                    }
                }
                catch
                {
                    // Skip folders that cannot be enumerated.
                }
            }

            // Make a few common names selectable even on a bare system; the
            // host falls back to its default face when one is absent.
            foreach (string fallback in FallbackFamilies)
            {
                if (!Families.ContainsKey(fallback))
                {
                    Families[fallback] = [];
                    DisplayNames[fallback] = fallback;
                }
            }

            RebuildNames();
        }
    }

    private static void RebuildNames()
    {
        _names = Families.Keys
            .Select(name => DisplayNames.TryGetValue(name, out string? display) ? display : name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static IEnumerable<string> FontDirectories()
    {
        HashSet<string> seen = new(StringComparer.OrdinalIgnoreCase);
        List<string> directories = [];

        void Add(string? path)
        {
            if (!string.IsNullOrWhiteSpace(path) && seen.Add(path))
            {
                directories.Add(path);
            }
        }

        if (OperatingSystem.IsWindows())
        {
            string windir = Environment.GetFolderPath(Environment.SpecialFolder.Windows);
            string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            Add(Path.Combine(windir, "Fonts"));
            Add(Path.Combine(local, "Microsoft", "Windows", "Fonts"));
        }
        else if (OperatingSystem.IsMacOS())
        {
            Add("/System/Library/Fonts");
            Add("/Library/Fonts");
            Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Fonts"));
        }
        else
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            Add("/usr/share/fonts");
            Add("/usr/local/share/fonts");
            Add("/usr/share/X11/fonts");
            Add(Path.Combine(home, ".fonts"));
            Add(Path.Combine(home, ".local", "share", "fonts"));
        }

        return directories;
    }

    private sealed class FontFace
    {
        public string Family = "";
        public int Weight = 400;
        public bool Italic;
    }

    private static List<FontFace> ReadFaces(FileStream stream)
    {
        List<FontFace> faces = [];
        byte[] header = new byte[12];
        ReadExactly(stream, 0, header);
        uint tag = ReadUInt32(header, 0);

        if (tag == 0x74746366) // ttcf
        {
            int count = (int)Math.Clamp(ReadUInt32(header, 8), 0u, 1024u);
            byte[] offsets = new byte[count * 4];
            ReadExactly(stream, 12, offsets);
            for (int i = 0; i < count; i++)
            {
                long offset = ReadUInt32(offsets, i * 4);
                FontFace? face = ReadFace(stream, offset);
                if (face is not null)
                {
                    faces.Add(face);
                }
            }

            return faces;
        }

        FontFace? single = ReadFace(stream, 0);
        if (single is not null)
        {
            faces.Add(single);
        }

        return faces;
    }

    private static FontFace? ReadFace(FileStream stream, long faceOffset)
    {
        byte[] offsetTable = new byte[12];
        ReadExactly(stream, faceOffset, offsetTable);
        int numTables = ReadUInt16(offsetTable, 4);
        if (numTables is <= 0 or > 512)
        {
            return null;
        }

        byte[] records = new byte[numTables * 16];
        ReadExactly(stream, faceOffset + 12, records);

        long nameOffset = -1;
        int nameLength = 0;
        long os2Offset = -1;
        int os2Length = 0;
        long headOffset = -1;
        int headLength = 0;

        for (int i = 0; i < numTables; i++)
        {
            int at = i * 16;
            string tag = Encoding.ASCII.GetString(records, at, 4);
            long offset = ReadUInt32(records, at + 8);
            int length = (int)Math.Min(ReadUInt32(records, at + 12), 4_000_000u);
            switch (tag)
            {
                case "name":
                    nameOffset = offset;
                    nameLength = length;
                    break;
                case "OS/2":
                    os2Offset = offset;
                    os2Length = length;
                    break;
                case "head":
                    headOffset = offset;
                    headLength = length;
                    break;
            }
        }

        if (nameOffset < 0 || nameLength <= 0)
        {
            return null;
        }

        byte[] nameTable = new byte[nameLength];
        ReadExactly(stream, nameOffset, nameTable);

        FontFace face = new();
        string? family = null;
        string? familyFallback = null;
        string? subfamily = null;

        int countNames = ReadUInt16(nameTable, 2);
        int stringOffset = ReadUInt16(nameTable, 4);

        for (int i = 0; i < countNames; i++)
        {
            int at = 6 + (i * 12);
            if (at + 12 > nameTable.Length)
            {
                break;
            }

            int platform = ReadUInt16(nameTable, at);
            int language = ReadUInt16(nameTable, at + 4);
            int nameId = ReadUInt16(nameTable, at + 6);
            int length = ReadUInt16(nameTable, at + 8);
            int offset = ReadUInt16(nameTable, at + 10);
            if (length <= 0 || stringOffset + offset + length > nameTable.Length)
            {
                continue;
            }

            if (nameId != 16 && nameId != 1 && nameId != 17 && nameId != 2)
            {
                continue;
            }

            string value = DecodeName(nameTable, stringOffset + offset, length, platform);
            if (string.IsNullOrWhiteSpace(value))
            {
                continue;
            }

            bool english = language is 0 or 0x409 or 0x809;
            if (nameId is 16 or 1)
            {
                if (nameId == 16 && english)
                {
                    family = value;
                }
                else if (familyFallback is null && (platform == 3 || english))
                {
                    familyFallback = value;
                }
            }
            else if (nameId is 17 or 2 && english)
            {
                subfamily = value;
            }
        }

        face.Family = family ?? familyFallback ?? "";
        string style = (subfamily ?? "").ToLowerInvariant();
        face.Weight = style.Contains("bold") || style.Contains("black") || style.Contains("heavy") ? 700 : 400;
        face.Italic = style.Contains("italic") || style.Contains("oblique");

        // OS/2 weight and selection flags are more reliable than the style name.
        if (os2Offset >= 0 && os2Length >= 64)
        {
            byte[] os2 = new byte[os2Length];
            ReadExactly(stream, os2Offset, os2);
            face.Weight = ReadUInt16(os2, 4);
            int selection = ReadUInt16(os2, 62);
            face.Italic |= (selection & 1) != 0;
            if ((selection & 32) != 0)
            {
                face.Weight = Math.Max(face.Weight, 700);
            }
        }

        if (headOffset >= 0 && headLength >= 46)
        {
            byte[] head = new byte[headLength];
            ReadExactly(stream, headOffset, head);
            int macStyle = ReadUInt16(head, 44);
            if ((macStyle & 1) != 0)
            {
                face.Weight = Math.Max(face.Weight, 700);
            }

            face.Italic |= (macStyle & 2) != 0;
        }

        if (face.Weight < 100 || face.Weight > 900)
        {
            face.Weight = 400;
        }

        return face;
    }

    private static string DecodeName(byte[] table, int offset, int length, int platform)
    {
        if (platform == 3 || platform == 0)
        {
            return Encoding.BigEndianUnicode.GetString(table, offset, length).Trim('\0', ' ');
        }

        return Encoding.ASCII.GetString(table, offset, length).Trim('\0', ' ');
    }

    private static void ReadExactly(Stream stream, long offset, byte[] buffer)
    {
        stream.Seek(offset, SeekOrigin.Begin);
        int read = 0;
        while (read < buffer.Length)
        {
            int chunk = stream.Read(buffer, read, buffer.Length - read);
            if (chunk <= 0)
            {
                throw new EndOfStreamException();
            }

            read += chunk;
        }
    }

    private static ushort ReadUInt16(byte[] data, int offset) =>
        (ushort)((data[offset] << 8) | data[offset + 1]);

    private static uint ReadUInt32(byte[] data, int offset) =>
        ((uint)data[offset] << 24) | ((uint)data[offset + 1] << 16) | ((uint)data[offset + 2] << 8) | data[offset + 3];
}
