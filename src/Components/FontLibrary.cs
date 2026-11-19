using System.IO;
using Donkeh.Windowing;
using VectorDotNet.Core;

namespace VectorDotNet.Components;

/// <summary>Registers local font files and families with the host font manager.</summary>
internal static class FontLibrary
{
    /// <summary>Adds a TTF, OTF, WOFF or WOFF2 file and returns its family name.</summary>
    public static string? Register(string path)
    {
        try
        {
            DonkehWindow? window = AppRootHost.Current;
            if (window is null || !File.Exists(path))
            {
                return null;
            }

            string family = SystemFontCatalog.ReadFamilyName(path) ?? Path.GetFileNameWithoutExtension(path);
            if (string.IsNullOrWhiteSpace(family))
            {
                return null;
            }

            window.Fonts.RegisterFont(family, path, 400, false);
            SystemFontCatalog.AddExternal(family, path, 400, false);
            return family;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Registers every discovered face of a family so text and previews can use it.</summary>
    public static void EnsureFamily(string family)
    {
        DonkehWindow? window = AppRootHost.Current;
        if (window is null || string.IsNullOrWhiteSpace(family))
        {
            return;
        }

        try
        {
            IReadOnlyList<FontFaceInfo> faces = SystemFontCatalog.FacesFor(family);
            foreach (FontFaceInfo face in faces)
            {
                window.Fonts.RegisterFont(family, face.Path, face.Weight, face.Italic);
            }
        }
        catch
        {
            // The host still falls back to its default face if registration fails.
        }
    }
}
