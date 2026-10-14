namespace DownloadsOrganizer.Core;

/// <summary>Built-in folders by file type. Used when no user rule matches.</summary>
public static class DefaultCategories
{
    public const string Other = "Other";
    public const string Folders = "Folders";

    static readonly (string Category, string Extensions)[] Map =
    [
        ("Documents", ".pdf .doc .docx .xls .xlsx .xlsm .ppt .pptx .hwp .hwpx .txt .md .rtf .odt .ods .odp .html .htm .pages .numbers .key"),
        ("Ebooks", ".epub .mobi .azw3 .acsm .djvu"),
        ("Images", ".jpg .jpeg .jfif .png .gif .webp .heic .heif .bmp .tif .tiff .svg .raw .cr2 .nef .dng .psd"),
        ("Media", ".mp4 .mov .mkv .avi .wmv .m4v .webm .mp3 .m4a .wav .flac .aac .ogg"),
        ("Subtitles", ".srt .vtt .smi .ass .ssa .sub"),
        ("Archives", ".zip .7z .rar .gz .tgz .tar .bz2 .xz .alz .egg .iso"),
        ("Installers", ".exe .msi .msix .msixbundle .appx .appxbundle .dmg .pkg .apk"),
        ("Data", ".csv .tsv .json .xml .yaml .yml .ipynb .py .r .sql .parquet .db .sqlite .log"),
        ("Fonts", ".ttf .otf .woff .woff2"),
    ];

    static readonly Dictionary<string, string> ByExtension = BuildIndex();

    /// <summary>Every folder name this app creates by default; these are never moved themselves.</summary>
    public static IReadOnlyList<string> Names { get; } = [.. Map.Select(m => m.Category), Other, Folders];

    /// <summary>Category for a file extension such as ".PDF" (case-insensitive); <see cref="Other"/> when unknown.</summary>
    public static string ForExtension(string extension) =>
        ByExtension.TryGetValue(extension, out var category) ? category : Other;

    static Dictionary<string, string> BuildIndex()
    {
        var index = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var (category, extensions) in Map)
        {
            foreach (var extension in extensions.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                index[extension] = category;
            }
        }
        return index;
    }
}
