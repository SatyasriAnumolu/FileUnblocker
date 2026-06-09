using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;

namespace FileUnblocker;

/// <summary>
/// Maps a file extension (or full path) to a frozen <see cref="Geometry"/>
/// so the DataGrid column can render a crisp vector icon at any DPI.
///
/// All path data uses a 20×20 coordinate space.
/// </summary>
[ValueConversion(typeof(string), typeof(Geometry))]
public sealed class FileTypeIconConverter : IValueConverter
{
    // ?? Singleton ?????????????????????????????????????????????????????????
 public static readonly FileTypeIconConverter Instance = new();

    // ?? Geometry cache (shared, frozen) ??????????????????????????????????
    private static readonly Dictionary<string, Geometry> _cache = new(StringComparer.OrdinalIgnoreCase);

    // ?? Path data by category (20×20 viewport) ????????????????????????????

    // Generic file page with folded corner
    private const string File =
 "M4,2 L13,2 L16,5 L16,18 L4,18 Z " +
        "M13,2 L13,5 L16,5 Z " +
        "M6,8 L14,8 M6,11 L14,11 M6,14 L11,14";

    // Archive / ZIP — box with lines
    private const string Archive =
        "M5,5 L15,5 L15,17 L5,17 Z " +
 "M8,5 L8,2 L12,2 L12,5 " +
        "M9,7 L11,7 M9,9 L11,9 M9,11 L11,11 M9,13 L11,13";

    // Executable — gear
  private const string Exe =
        "M10,6.5 A3.5,3.5 0 1 1 10,13.5 A3.5,3.5 0 1 1 10,6.5 Z " +
        "M10,3 L10,5 M10,15 L10,17 " +
     "M3,10 L5,10 M15,10 L17,10 " +
  "M4.9,4.9 L6.4,6.4 M13.6,13.6 L15.1,15.1 " +
        "M15.1,4.9 L13.6,6.4 M6.4,13.6 L4.9,15.1";

// PDF — document with bookmark
    private const string Pdf =
"M4,2 L14,2 L14,18 L4,18 Z " +
        "M14,2 L16,4 L16,18 L14,18 " +
        "M6,7 L12,7 M6,10 L12,10 M6,13 L9,13";

    // Image — landscape in frame
private const string Image =
      "M2,4 L18,4 L18,16 L2,16 Z " +
   "M2,12 L6,8 L10,12 L13,9 L18,14 " +
     "M14,7 A1.5,1.5 0 1 1 14,7.01";

    // Video — film strip
    private const string Video =
        "M2,5 L18,5 L18,15 L2,15 Z " +
 "M2,7 L5,7 M15,7 L18,7 M2,10 L5,10 M15,10 L18,10 M2,13 L5,13 M15,13 L18,13 " +
        "M8,7 L13,10 L8,13 Z";

    // Audio — waveform / note
    private const string Audio =
        "M6,14 L6,6 L10,8 L10,16 Z " +
   "M11,5 A5,5 0 0 1 11,15 " +
        "M13,7 A3,3 0 0 1 13,13";

    // Word / doc
    private const string Word =
     "M3,2 L12,2 L17,7 L17,18 L3,18 Z " +
        "M12,2 L12,7 L17,7 " +
        "M5,10 L15,10 M5,13 L15,13 M5,7 L10,7";

    // Spreadsheet / CSV
    private const string Spreadsheet =
      "M2,4 L18,4 L18,16 L2,16 Z " +
        "M2,8 L18,8 M2,12 L18,12 " +
        "M7,4 L7,16 M13,4 L13,16";

    // Text / log / markdown
    private const string Text =
        "M4,2 L16,2 L16,18 L4,18 Z " +
        "M6,6 L14,6 M6,9 L14,9 M6,12 L14,12 M6,15 L11,15";

    // Script / code / config
    private const string Script =
        "M4,2 L13,2 L16,5 L16,18 L4,18 Z " +
      "M13,2 L13,5 L16,5 " +
"M7,8 L5,10 L7,12 M13,8 L15,10 L13,12 M11,7 L9,13";

    // Config / settings (wrench)
    private const string Config =
        "M14.5,3.5 L13,5 L11,7 L13,9 L15,7 L16.5,5.5 A5,5 0 1 1 14.5,3.5 Z " +
        "M4,16 L10,10 M10,10 L12,8";

  // Folder
    private const string Folder =
   "M2,6 L7,6 L8.5,4 L18,4 L18,16 L2,16 Z";

    // Blocked-file lock overlay
    private const string Lock =
        "M10,2 A4,4 0 0 1 14,6 L14,9 L6,9 L6,6 A4,4 0 0 1 10,2 Z " +
      "M5,9 L15,9 L15,18 L5,18 Z " +
        "M10,12 L10,15";

    // ?? Conversion ????????????????????????????????????????????????????????
 public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var input = value as string ?? string.Empty;
        var ext   = Path.GetExtension(input).ToLowerInvariant();

        if (!_cache.TryGetValue(ext, out var geo))
        {
 var data = CategoryData(ext);
  geo = Geometry.Parse(data);
       geo.Freeze();
     _cache[ext] = geo;
        }
 return geo;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    // ?? Extension ? path data ?????????????????????????????????????????????
    private static string CategoryData(string ext) => ext switch
    {
        ".zip" or ".cab" or ".7z" or ".rar" or ".tar" or ".gz" or ".bz2" => Archive,
        ".exe" or ".msi" or ".dll" or ".sys"       => Exe,
".pdf"         => Pdf,
      ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp"
            or ".webp" or ".svg" or ".tiff" or ".ico"       => Image,
    ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv" or ".flv"   => Video,
        ".mp3" or ".wav" or ".flac" or ".aac" or ".ogg" or ".m4a"       => Audio,
        ".doc" or ".docx" or ".odt" or ".rtf"  => Word,
 ".xls" or ".xlsx" or ".csv" or ".ods"         => Spreadsheet,
".txt" or ".log" or ".md" or ".rst" => Text,
  ".ps1" or ".bat" or ".cmd" or ".sh" or ".bash"    => Script,
  ".json" or ".xml" or ".yaml" or ".toml" or ".ini" or ".cfg"   => Config,
        _          => File,
    };
}

/// <summary>
/// Maps a file extension to an accent <see cref="SolidColorBrush"/> (default)
/// or a raw <see cref="Color"/> when <c>parameter</c> is <c>"color"</c>.
/// </summary>
[ValueConversion(typeof(string), typeof(object))]
public sealed class FileTypeColorConverter : IValueConverter
{
    public static readonly FileTypeColorConverter Instance = new();

    private static readonly Dictionary<string, SolidColorBrush> _brushCache =
   new(StringComparer.OrdinalIgnoreCase);

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var input = value as string ?? string.Empty;
        var ext   = Path.GetExtension(input).ToLowerInvariant();
        var color = CategoryColor(ext);

        // When ConverterParameter="color", return raw Color (for DropShadowEffect etc.)
        if (parameter is string p && p.Equals("color", StringComparison.OrdinalIgnoreCase))
            return color;

        if (!_brushCache.TryGetValue(ext, out var brush))
        {
         brush = new SolidColorBrush(color);
         brush.Freeze();
     _brushCache[ext] = brush;
        }
     return brush;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();

    private static Color CategoryColor(string ext) => ext switch
    {
        ".zip" or ".cab" or ".7z" or ".rar"    => Color.FromRgb(0xF5, 0x9E, 0x0B),
        ".exe" or ".msi" or ".dll" or ".sys"  => Color.FromRgb(0xA7, 0x8B, 0xFA),
      ".pdf"=> Color.FromRgb(0xF8, 0x71, 0x71),
        ".jpg" or ".jpeg" or ".png" or ".gif" or ".bmp" or ".webp" or ".svg" => Color.FromRgb(0x34, 0xD3, 0x99),
        ".mp4" or ".mkv" or ".avi" or ".mov" or ".wmv"               => Color.FromRgb(0x60, 0xA5, 0xFA),
        ".mp3" or ".wav" or ".flac" or ".aac"            => Color.FromRgb(0xF4, 0x72, 0xB6),
        ".doc" or ".docx" or ".odt"           => Color.FromRgb(0x38, 0xBD, 0xF8),
        ".xls" or ".xlsx" or ".csv"=> Color.FromRgb(0x4A, 0xDE, 0x80),
".txt" or ".log" or ".md"             => Color.FromRgb(0x94, 0xA3, 0xB8),
     ".ps1" or ".bat" or ".cmd" or ".sh"   => Color.FromRgb(0xFB, 0xBF, 0x24),
    ".json" or ".xml" or ".yaml" or ".toml"  => Color.FromRgb(0x67, 0xE8, 0xF9),
        _     => Color.FromRgb(0x94, 0xA3, 0xB8),
    };
}
