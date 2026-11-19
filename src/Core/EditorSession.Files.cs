using System.IO;

namespace VectorDotNet.Core;

/// <summary>Whether the browser is choosing an existing file or a destination.</summary>
public enum FileBrowserMode
{
    /// <summary>Pick an existing file.</summary>
    Open,

    /// <summary>Choose a folder and file name.</summary>
    Save,
}

/// <summary>What the browser will do with the chosen path.</summary>
public enum FileBrowserPurpose
{
    /// <summary>Import an SVG document.</summary>
    ImportSvg,

    /// <summary>Export the active artboard as SVG or SVGZ.</summary>
    ExportSvg,

    /// <summary>Register a font file with the host font manager.</summary>
    Font,
}

/// <summary>One row in the file browser.</summary>
public sealed record FileEntry(string Name, string Path, bool IsFolder, long Size);

/// <summary>In-app file browsing for SVG and font files.</summary>
public sealed partial class EditorSession
{
    private readonly List<FileEntry> _browserEntries = [];
    private readonly List<(string Label, string Path)> _browserPlaces = [];
    private string _lastBrowserFolder = "";

    /// <summary>Whether the file browser is open.</summary>
    public bool ShowFileBrowser { get; private set; }

    /// <summary>Whether the browser is opening or saving.</summary>
    public FileBrowserMode BrowserMode { get; private set; }

    /// <summary>What the chosen file will be used for.</summary>
    public FileBrowserPurpose BrowserPurpose { get; private set; }

    /// <summary>Folder currently shown.</summary>
    public string BrowserFolder { get; private set; } = "";

    /// <summary>Destination file name shown in save mode.</summary>
    public string BrowserName { get; set; } = "";

    /// <summary>Full path of the selected file, or empty.</summary>
    public string BrowserSelected { get; private set; } = "";

    /// <summary>Status or error text in the browser footer.</summary>
    public string BrowserStatus { get; private set; } = "";

    /// <summary>Rows currently listed.</summary>
    public IReadOnlyList<FileEntry> BrowserEntries => _browserEntries;

    /// <summary>Shortcut folders.</summary>
    public IReadOnlyList<(string Label, string Path)> BrowserPlaces => _browserPlaces;

    /// <summary>Registers a font file and returns its family name, or null.</summary>
    public Func<string, string?>? RegisterFontFile { get; set; }

    /// <summary>Family used by newly created text.</summary>
    public string DefaultFontFamily { get; set; } = "Segoe UI";

    /// <summary>Whether the searchable font picker is open.</summary>
    public bool ShowFontPicker { get; private set; }

    /// <summary>Current search text in the font picker.</summary>
    public string FontSearch { get; private set; } = "";

    /// <summary>Family shown as selected in the picker.</summary>
    public string CurrentFontFamily =>
        Selection.OfType<TextObject>().FirstOrDefault()?.FontFamily ?? DefaultFontFamily;

    /// <summary>Font families matching the search, sorted.</summary>
    public IReadOnlyList<string> FilteredFontFamilies
    {
        get
        {
            IReadOnlyList<string> all = SystemFontCatalog.FamilyNames;
            if (string.IsNullOrWhiteSpace(FontSearch))
            {
                return all;
            }

            string search = FontSearch.Trim();
            return all.Where(name => name.Contains(search, StringComparison.OrdinalIgnoreCase)).ToList();
        }
    }

    /// <summary>Opens the searchable font picker.</summary>
    public void OpenFontPicker()
    {
        ShowFontPicker = true;
        FontSearch = "";
        OpenMenu = null;
        Notify();
    }

    /// <summary>Closes the font picker.</summary>
    public void CloseFontPicker()
    {
        ShowFontPicker = false;
        Notify();
    }

    /// <summary>Updates the font search text.</summary>
    public void SetFontSearch(string text)
    {
        FontSearch = text;
        Notify();
    }

    /// <summary>Applies a family chosen from the picker.</summary>
    public void ApplyFontFamily(string family)
    {
        UseFontFamily(family);
        ShowFontPicker = false;
        _toolHint = family;
        Notify();
    }

    /// <summary>Switches from the font picker to the file browser for a font file.</summary>
    public void LoadFontFromPicker()
    {
        ShowFontPicker = false;
        OpenFontBrowser();
    }

    /// <summary>Title for the browser.</summary>
    public string BrowserTitle => BrowserPurpose switch
    {
        FileBrowserPurpose.ImportSvg => "Import SVG",
        FileBrowserPurpose.ExportSvg => "Export SVG",
        _ => "Load Font",
    };

    /// <summary>Label for the confirm button.</summary>
    public string BrowserConfirmLabel => BrowserPurpose switch
    {
        FileBrowserPurpose.ImportSvg => "Import",
        FileBrowserPurpose.ExportSvg => "Export",
        _ => "Load Font",
    };

    /// <summary>Extensions accepted by the current purpose.</summary>
    public string[] BrowserExtensions => BrowserPurpose switch
    {
        FileBrowserPurpose.Font => [".ttf", ".otf", ".woff", ".woff2"],
        _ => [".svg", ".svgz"],
    };

    /// <summary>Opens the browser for a file operation.</summary>
    public void OpenFileBrowser(FileBrowserPurpose purpose)
    {
        BrowserPurpose = purpose;
        BrowserMode = purpose == FileBrowserPurpose.ExportSvg ? FileBrowserMode.Save : FileBrowserMode.Open;
        BrowserStatus = string.Empty;
        BrowserSelected = string.Empty;
        BrowserName = purpose == FileBrowserPurpose.ExportSvg ? Document.Name + ".svg" : string.Empty;

        string folder = string.IsNullOrWhiteSpace(_lastBrowserFolder) ? DefaultBrowserFolder() : _lastBrowserFolder;
        if (!Directory.Exists(folder))
        {
            folder = DefaultBrowserFolder();
        }

        BrowserFolder = folder;
        BuildPlaces();
        RefreshBrowser();
        ShowFileBrowser = true;
        OpenMenu = null;
        Notify();
    }

    /// <summary>Closes the browser.</summary>
    public void CloseFileBrowser()
    {
        if (!ShowFileBrowser)
        {
            return;
        }

        ShowFileBrowser = false;
        Notify();
    }

    /// <summary>Moves to a folder by path.</summary>
    public void NavigateBrowser(string folder)
    {
        try
        {
            string full = Path.GetFullPath(folder.Trim().Trim('"'));
            if (!Directory.Exists(full))
            {
                BrowserStatus = "Folder not found.";
                Notify();
                return;
            }

            BrowserFolder = full;
            _lastBrowserFolder = full;
            BrowserSelected = string.Empty;
            BrowserStatus = string.Empty;
            RefreshBrowser();
            Notify();
        }
        catch (Exception error)
        {
            BrowserStatus = error.Message;
            Notify();
        }
    }

    /// <summary>Moves to the parent folder.</summary>
    public void BrowserGoUp()
    {
        DirectoryInfo? parent = Directory.GetParent(BrowserFolder);
        if (parent is null)
        {
            return;
        }

        NavigateBrowser(parent.FullName);
    }

    /// <summary>Uses one of the shortcut folders.</summary>
    public void BrowserUsePlace(string path) => NavigateBrowser(path);

    /// <summary>Handles a row click.</summary>
    public void BrowserClickEntry(FileEntry entry)
    {
        if (entry.IsFolder)
        {
            NavigateBrowser(entry.Path);
            return;
        }

        BrowserSelected = entry.Path;
        BrowserName = entry.Name;
        BrowserStatus = string.Empty;
        Notify();
    }

    /// <summary>Confirms the current selection.</summary>
    public void BrowserConfirm()
    {
        string path;
        if (BrowserMode == FileBrowserMode.Save)
        {
            if (string.IsNullOrWhiteSpace(BrowserName))
            {
                BrowserStatus = "Enter a file name.";
                Notify();
                return;
            }

            path = Path.Combine(BrowserFolder, BrowserName.Trim());
            if (BrowserPurpose == FileBrowserPurpose.ExportSvg
                && !path.EndsWith(".svg", StringComparison.OrdinalIgnoreCase)
                && !path.EndsWith(".svgz", StringComparison.OrdinalIgnoreCase))
            {
                path += ".svg";
            }
        }
        else
        {
            path = BrowserSelected;
            if (string.IsNullOrWhiteSpace(path))
            {
                path = Path.Combine(BrowserFolder, BrowserName);
            }

            if (!File.Exists(path))
            {
                BrowserStatus = "Choose a file.";
                Notify();
                return;
            }
        }

        switch (BrowserPurpose)
        {
            case FileBrowserPurpose.ImportSvg:
                ImportPath = path;
                ConfirmImport();
                if (ImportStatus.Length == 0)
                {
                    ShowFileBrowser = false;
                }
                else
                {
                    BrowserStatus = ImportStatus;
                }

                break;

            case FileBrowserPurpose.ExportSvg:
                ExportPath = path;
                ConfirmExport();
                if (ExportStatus.StartsWith("Saved", StringComparison.Ordinal))
                {
                    ShowFileBrowser = false;
                }
                else
                {
                    BrowserStatus = ExportStatus;
                }

                break;

            case FileBrowserPurpose.Font:
                string? family = RegisterFontFile?.Invoke(path);
                if (family is null)
                {
                    BrowserStatus = "Could not read that font file.";
                }
                else
                {
                    UseFontFamily(family);
                    _toolHint = $"Font {family} loaded";
                    ShowFileBrowser = false;
                }

                break;
        }

        Notify();
    }

    /// <summary>Applies a loaded font family to the default and selected text.</summary>
    public void UseFontFamily(string family)
    {
        if (string.IsNullOrWhiteSpace(family))
        {
            return;
        }

        DefaultFontFamily = family;
        foreach (TextObject text in Selection.OfType<TextObject>())
        {
            text.FontFamily = family;
        }

        Notify();
    }

    private void RefreshBrowser()
    {
        _browserEntries.Clear();
        BrowserStatus = string.Empty;

        try
        {
            foreach (string directory in Directory.GetDirectories(BrowserFolder))
            {
                DirectoryInfo info = new(directory);
                _browserEntries.Add(new FileEntry(info.Name, info.FullName, true, 0));
            }

            string[] extensions = BrowserExtensions;
            foreach (string file in Directory.GetFiles(BrowserFolder))
            {
                string extension = Path.GetExtension(file);
                if (!extensions.Contains(extension, StringComparer.OrdinalIgnoreCase))
                {
                    continue;
                }

                FileInfo info = new(file);
                _browserEntries.Add(new FileEntry(info.Name, info.FullName, false, info.Length));
            }

            _browserEntries.Sort((a, b) => a.IsFolder != b.IsFolder
                ? (a.IsFolder ? -1 : 1)
                : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase));
        }
        catch (Exception error)
        {
            BrowserStatus = "Cannot read this folder: " + error.Message;
        }
    }

    private void BuildPlaces()
    {
        _browserPlaces.Clear();
        Add("Documents", Environment.SpecialFolder.MyDocuments);
        Add("Desktop", Environment.SpecialFolder.DesktopDirectory);
        Add("Downloads", Environment.SpecialFolder.UserProfile, "Downloads");
        Add("Pictures", Environment.SpecialFolder.MyPictures);
        Add("Home", Environment.SpecialFolder.UserProfile);

        void Add(string label, Environment.SpecialFolder folder, string? child = null)
        {
            string path = Environment.GetFolderPath(folder);
            if (child is not null)
            {
                path = Path.Combine(path, child);
            }

            if (Directory.Exists(path))
            {
                _browserPlaces.Add((label, path));
            }
        }
    }

    private static string DefaultBrowserFolder()
    {
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        return Directory.Exists(documents) ? documents : Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
    }
}
