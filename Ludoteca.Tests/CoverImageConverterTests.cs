using System.Windows.Media;
using System.Windows.Media.Imaging;
using Ludoteca.Converters;

namespace Ludoteca.Tests;

public sealed class CoverImageConverterTests : IDisposable
{
    private readonly string _directory =
        Path.Combine(Path.GetTempPath(), $"jogoteca-cover-tests-{Guid.NewGuid():N}");

    public CoverImageConverterTests()
    {
        Directory.CreateDirectory(_directory);
    }

    [Theory]
    // Landscape RAWG picture in a portrait card: the height decides.
    [InlineData(1920, 1080, 168, 200, 1.0, 356)]
    // Portrait IGDB cover in a portrait card: the width decides.
    [InlineData(1056, 1496, 168, 200, 1.0, 168)]
    // Screen at 150%: the image is decoded larger.
    [InlineData(1920, 1080, 168, 200, 1.5, 534)]
    // Already smaller than the card: kept as is.
    [InlineData(100, 140, 168, 200, 1.0, 0)]
    public void DecodesTheSmallestSizeThatCoversTheCard(
        int pixelWidth, int pixelHeight, double boxWidth, double boxHeight, double dpiScale, int expected)
    {
        Assert.Equal(
            expected,
            CoverImageConverter.DecodeWidthToCover(pixelWidth, pixelHeight, boxWidth, boxHeight, dpiScale));
    }

    [Fact]
    public void LoadsLocalCoverReducedAndWithoutLockingTheFile()
    {
        var path = Path.Combine(_directory, "cover.png");
        WritePng(path, 1000, 500);

        var image = CoverImageConverter.LoadLocalCover(path, 100, 100, 1.0);

        Assert.NotNull(image);
        Assert.True(image.IsFrozen);
        Assert.Equal(200, image.PixelWidth);
        Assert.Equal(100, image.PixelHeight);
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    [Fact]
    public void MissingOrBrokenCoverReturnsNothing()
    {
        var broken = Path.Combine(_directory, "broken.jpg");
        File.WriteAllText(broken, "not an image");

        Assert.Null(CoverImageConverter.LoadLocalCover(Path.Combine(_directory, "missing.jpg"), 100, 100, 1.0));
        Assert.Null(CoverImageConverter.LoadLocalCover(broken, 100, 100, 1.0));
    }

    private static void WritePng(string path, int width, int height)
    {
        var pixels = new byte[width * height * 4];
        var bitmap = BitmapSource.Create(width, height, 96, 96, PixelFormats.Bgra32, null, pixels, width * 4);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(path);
        encoder.Save(stream);
    }

    public void Dispose()
    {
        Directory.Delete(_directory, recursive: true);
    }
}
