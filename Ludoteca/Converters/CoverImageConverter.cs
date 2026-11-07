using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Ludoteca.Converters;

// Loads covers at the size they are shown instead of their original size.
// ConverterParameter is the box the cover fills, in logical pixels: "168x200".
public sealed class CoverImageConverter : IValueConverter
{
    private const int CacheCapacity = 300;
    private static readonly Dictionary<string, LinkedListNode<(string Key, ImageSource Image)>> Cache = [];
    private static readonly LinkedList<(string Key, ImageSource Image)> Recent = [];

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string source || string.IsNullOrWhiteSpace(source) ||
            !TryParseBox(parameter as string, out var boxWidth, out var boxHeight))
        {
            return null;
        }

        var dpiScale = CurrentDpiScale();
        var key = $"{source}|{boxWidth}x{boxHeight}|{dpiScale:0.##}";
        if (Cache.TryGetValue(key, out var cached))
        {
            Recent.Remove(cached);
            Recent.AddFirst(cached);
            return cached.Value.Image;
        }

        ImageSource? image = Path.IsPathRooted(source) && !source.StartsWith("http", StringComparison.OrdinalIgnoreCase)
            ? LoadLocalCover(source, boxWidth, boxHeight, dpiScale)
            : LoadRemoteCover(source, boxHeight, dpiScale);
        if (image is not null)
        {
            Remember(key, image);
        }

        return image;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        Binding.DoNothing;

    public static void ClearCache()
    {
        Cache.Clear();
        Recent.Clear();
    }

    public static BitmapSource? LoadLocalCover(string path, double boxWidth, double boxHeight, double dpiScale)
    {
        try
        {
            if (!File.Exists(path))
            {
                return null;
            }

            // Read once into memory so the file is never kept open (it can be deleted or replaced later).
            var bytes = File.ReadAllBytes(path);
            var frame = BitmapDecoder.Create(
                new MemoryStream(bytes),
                BitmapCreateOptions.DelayCreation,
                BitmapCacheOption.None).Frames[0];

            var image = new BitmapImage();
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = new MemoryStream(bytes);
            var decodeWidth = DecodeWidthToCover(frame.PixelWidth, frame.PixelHeight, boxWidth, boxHeight, dpiScale);
            if (decodeWidth > 0)
            {
                image.DecodePixelWidth = decodeWidth;
            }

            image.EndInit();
            image.Freeze();
            return image;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException or
                                       FileFormatException or ArgumentException or InvalidOperationException)
        {
            return null;
        }
    }

    // Smallest width that still covers the whole box once stretched with UniformToFill; 0 keeps the original.
    public static int DecodeWidthToCover(int pixelWidth, int pixelHeight, double boxWidth, double boxHeight, double dpiScale)
    {
        if (pixelWidth <= 0 || pixelHeight <= 0)
        {
            return 0;
        }

        var scale = Math.Max(boxWidth * dpiScale / pixelWidth, boxHeight * dpiScale / pixelHeight);
        return scale >= 1 ? 0 : (int)Math.Ceiling(pixelWidth * scale);
    }

    private static ImageSource? LoadRemoteCover(string url, double boxHeight, double dpiScale)
    {
        if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            return null;
        }

        var image = new BitmapImage();
        image.BeginInit();
        image.UriSource = uri;
        // RAWG sends large landscape pictures: in a portrait card the height decides the size.
        // IGDB covers are already small and are left as they are.
        if (uri.Host.EndsWith("rawg.io", StringComparison.OrdinalIgnoreCase))
        {
            image.DecodePixelHeight = (int)Math.Ceiling(boxHeight * dpiScale);
        }

        image.EndInit();
        return image;
    }

    private static void Remember(string key, ImageSource image)
    {
        var node = Recent.AddFirst((key, image));
        Cache[key] = node;
        if (Recent.Count > CacheCapacity && Recent.Last is { } oldest)
        {
            Recent.RemoveLast();
            Cache.Remove(oldest.Value.Key);
        }
    }

    private static double CurrentDpiScale()
    {
        var window = Application.Current?.MainWindow;
        return window is null ? 1.0 : Math.Max(1.0, VisualTreeHelper.GetDpi(window).DpiScaleX);
    }

    private static bool TryParseBox(string? parameter, out double width, out double height)
    {
        width = height = 0;
        var parts = parameter?.Split('x');
        return parts is { Length: 2 } &&
               double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out width) &&
               double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out height) &&
               width > 0 && height > 0;
    }
}
