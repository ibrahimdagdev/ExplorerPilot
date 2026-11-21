using ExplorerPilot.Core.Enums;

namespace ExplorerPilot.Core.Scanning;

/// <summary>
/// Maps normalized file extensions to categories. Kept separate from scanning I/O.
/// </summary>
public static class FileCategoryMapper
{
    private static readonly Dictionary<string, FileCategory> Map =
        new(StringComparer.OrdinalIgnoreCase)
        {
            // Videos
            [".mp4"] = FileCategory.Videos,
            [".mkv"] = FileCategory.Videos,
            [".avi"] = FileCategory.Videos,
            [".mov"] = FileCategory.Videos,
            [".wmv"] = FileCategory.Videos,
            [".flv"] = FileCategory.Videos,
            [".webm"] = FileCategory.Videos,
            [".m4v"] = FileCategory.Videos,
            [".mpeg"] = FileCategory.Videos,
            [".mpg"] = FileCategory.Videos,
            [".ts"] = FileCategory.Videos,
            [".m2ts"] = FileCategory.Videos,
            [".3gp"] = FileCategory.Videos,

            // Images
            [".jpg"] = FileCategory.Images,
            [".jpeg"] = FileCategory.Images,
            [".png"] = FileCategory.Images,
            [".gif"] = FileCategory.Images,
            [".bmp"] = FileCategory.Images,
            [".webp"] = FileCategory.Images,
            [".tif"] = FileCategory.Images,
            [".tiff"] = FileCategory.Images,
            [".heic"] = FileCategory.Images,
            [".heif"] = FileCategory.Images,
            [".svg"] = FileCategory.Images,
            [".ico"] = FileCategory.Images,
            [".raw"] = FileCategory.Images,
            [".cr2"] = FileCategory.Images,
            [".nef"] = FileCategory.Images,
            [".dng"] = FileCategory.Images,

            // Audio
            [".mp3"] = FileCategory.Audio,
            [".wav"] = FileCategory.Audio,
            [".flac"] = FileCategory.Audio,
            [".aac"] = FileCategory.Audio,
            [".ogg"] = FileCategory.Audio,
            [".wma"] = FileCategory.Audio,
            [".m4a"] = FileCategory.Audio,
            [".aiff"] = FileCategory.Audio,
            [".opus"] = FileCategory.Audio,
            [".mid"] = FileCategory.Audio,
            [".midi"] = FileCategory.Audio,

            // Documents
            [".pdf"] = FileCategory.Documents,
            [".doc"] = FileCategory.Documents,
            [".docx"] = FileCategory.Documents,
            [".xls"] = FileCategory.Documents,
            [".xlsx"] = FileCategory.Documents,
            [".ppt"] = FileCategory.Documents,
            [".pptx"] = FileCategory.Documents,
            [".txt"] = FileCategory.Documents,
            [".rtf"] = FileCategory.Documents,
            [".odt"] = FileCategory.Documents,
            [".ods"] = FileCategory.Documents,
            [".odp"] = FileCategory.Documents,
            [".csv"] = FileCategory.Documents,
            [".md"] = FileCategory.Documents,
            [".html"] = FileCategory.Documents,
            [".htm"] = FileCategory.Documents,
            [".xml"] = FileCategory.Documents,
            [".json"] = FileCategory.Documents,
            [".epub"] = FileCategory.Documents,

            // Archives
            [".zip"] = FileCategory.Archives,
            [".rar"] = FileCategory.Archives,
            [".7z"] = FileCategory.Archives,
            [".tar"] = FileCategory.Archives,
            [".gz"] = FileCategory.Archives,
            [".bz2"] = FileCategory.Archives,
            [".xz"] = FileCategory.Archives,
            [".iso"] = FileCategory.Archives,
            [".cab"] = FileCategory.Archives,
            [".lz"] = FileCategory.Archives,
            [".tgz"] = FileCategory.Archives,

            // Applications
            [".exe"] = FileCategory.Applications,
            [".msi"] = FileCategory.Applications,
            [".dll"] = FileCategory.Applications,
            [".bat"] = FileCategory.Applications,
            [".cmd"] = FileCategory.Applications,
            [".ps1"] = FileCategory.Applications,
            [".com"] = FileCategory.Applications,
            [".scr"] = FileCategory.Applications,
            [".apk"] = FileCategory.Applications,
            [".appx"] = FileCategory.Applications,
            [".msix"] = FileCategory.Applications,
            [".jar"] = FileCategory.Applications
        };

    public static FileCategory GetCategory(string? normalizedExtension)
    {
        if (string.IsNullOrEmpty(normalizedExtension))
        {
            return FileCategory.Other;
        }

        return Map.TryGetValue(normalizedExtension, out var category)
            ? category
            : FileCategory.Other;
    }

    /// <summary>
    /// Normalizes an extension for dictionary keys: lowercase with leading dot, or empty when none.
    /// Accepts a file name (<c>photo.JPG</c>) or an explicit extension (<c>.jpg</c> / <c>.JPG</c>).
    /// </summary>
    public static string NormalizeExtension(string? fileNameOrExtension)
    {
        if (string.IsNullOrWhiteSpace(fileNameOrExtension))
        {
            return string.Empty;
        }

        var value = fileNameOrExtension.Trim();

        string extension;
        if (value.StartsWith('.') && value.IndexOf('.') == value.LastIndexOf('.')
            && value.IndexOfAny(['/', '\\']) < 0)
        {
            // Explicit single-segment extension such as ".jpg" or ".MP4".
            extension = value;
        }
        else
        {
            extension = Path.GetExtension(value);
        }

        return string.IsNullOrEmpty(extension) ? string.Empty : extension.ToLowerInvariant();
    }
}
