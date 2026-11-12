using RecompOne.Runtime.Host.Window;
using Xunit;

namespace RecompOne.Runtime.Tests;

public sealed class OutputOverlayTests : IDisposable
{
    // A 2x1 PNG: opaque red, then half-transparent green.
    internal const string Png = "iVBORw0KGgoAAAANSUhEUgAAAAIAAAABCAYAAAD0In+KAAAAEUlEQVR4nGP4z8Dwn+E/QwMAEHkDfiHA/Y0AAAAASUVORK5CYII=";
    readonly string _directory = Path.Combine(Path.GetTempPath(), "recomp-overlay-" + Guid.NewGuid());
    readonly List<uint> _deleted = [];
    readonly List<(byte[] Pixels, int Width, int Height)> _uploads = [];
    readonly OutputOverlay _overlay;

    public OutputOverlayTests()
    {
        Directory.CreateDirectory(_directory);
        _overlay = new OutputOverlay((pixels, w, h) =>
        {
            _uploads.Add((pixels, w, h));
            return (uint)_uploads.Count;
        }, _deleted.Add);
    }

    string WriteImage(string name = "bezel.png")
    {
        var path = Path.Combine(_directory, name);
        File.WriteAllBytes(path, Convert.FromBase64String(Png));
        return path;
    }

    [Fact]
    public void LoadsNonSquareImageWithoutCroppingOrLosingTransparency()
    {
        var image = _overlay.Get(WriteImage(), true);

        Assert.Equal((1u, 2, 1), image);
        var upload = Assert.Single(_uploads);
        Assert.Equal(2, upload.Width);
        Assert.Equal(1, upload.Height);
        Assert.Equal(new byte[] { 255, 0, 0, 255, 0, 255, 0, 128 }, upload.Pixels);
        Assert.Empty(_overlay.Error);
    }

    [Fact]
    public void LoadsJpegWithOpaqueAlpha()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Fixtures", "red.jpg");

        Assert.Equal((1u, 3, 2), _overlay.Get(path, true));
        var upload = Assert.Single(_uploads);
        Assert.Equal(24, upload.Pixels.Length);
        for (var i = 3; i < upload.Pixels.Length; i += 4)
            Assert.Equal(255, upload.Pixels[i]);
    }

    [Fact]
    public void DirectoryCannotBeUsedAsImage()
    {
        Assert.Equal((0u, 0, 0), _overlay.Get(_directory, true));
        Assert.NotEmpty(_overlay.Error);
    }

    [Fact]
    public void DoesNotReadOrUploadAgainEveryFrame()
    {
        var path = WriteImage();
        _overlay.Get(path, true);
        File.Delete(path);

        Assert.Equal((1u, 2, 1), _overlay.Get(path, true));
        Assert.Single(_uploads);
    }

    [Fact]
    public void ReplacementReleasesPreviousTexture()
    {
        _overlay.Get(WriteImage("first.png"), true);

        Assert.Equal((2u, 2, 1), _overlay.Get(WriteImage("second.png"), true));
        Assert.Equal(new uint[] { 1 }, _deleted);
    }

    [Fact]
    public void DisablingReleasesTextureAndReenablingReloadsIt()
    {
        var path = WriteImage();
        _overlay.Get(path, true);

        Assert.Equal((0u, 0, 0), _overlay.Get(path, false));
        Assert.Equal(new uint[] { 1 }, _deleted);
        Assert.Equal((2u, 2, 1), _overlay.Get(path, true));
    }

    [Fact]
    public void ClearingPathReleasesTexture()
    {
        _overlay.Get(WriteImage(), true);

        Assert.Equal((0u, 0, 0), _overlay.Get("", true));
        Assert.Equal(new uint[] { 1 }, _deleted);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void EmptyPathDoesNotAttemptToLoad(string path)
    {
        Assert.Equal((0u, 0, 0), _overlay.Get(path, true));
        Assert.Empty(_uploads);
        Assert.Empty(_overlay.Error);
    }

    [Fact]
    public void MissingFileIsCachedUntilExplicitReload()
    {
        var path = Path.Combine(_directory, "missing.png");
        Assert.Equal((0u, 0, 0), _overlay.Get(path, true));
        Assert.NotEmpty(_overlay.Error);
        WriteImage("missing.png");

        Assert.Equal((0u, 0, 0), _overlay.Get(path, true));
        _overlay.Clear();
        Assert.Equal((1u, 2, 1), _overlay.Get(path, true));
        Assert.Empty(_overlay.Error);
    }

    [Fact]
    public void InvalidImageDoesNotEscapeIntoRenderingLoop()
    {
        var path = Path.Combine(_directory, "invalid.png");
        File.WriteAllText(path, "not an image");

        Assert.Equal((0u, 0, 0), _overlay.Get(path, true));
        Assert.NotEmpty(_overlay.Error);
        Assert.Empty(_uploads);
    }

    [Fact]
    public void InvalidReplacementReleasesPreviousTexture()
    {
        _overlay.Get(WriteImage(), true);

        Assert.Equal((0u, 0, 0), _overlay.Get(Path.Combine(_directory, "missing.png"), true));
        Assert.Equal(new uint[] { 1 }, _deleted);
        Assert.NotEmpty(_overlay.Error);
    }

    [Fact]
    public void UploadFailureCanBeRetriedWithoutChangingPath()
    {
        var ready = false;
        using var overlay = new OutputOverlay((_, _, _) => ready ? 7u : 0u, _deleted.Add);
        var path = WriteImage();

        Assert.Equal((0u, 0, 0), overlay.Get(path, true));
        Assert.NotEmpty(overlay.Error);
        ready = true;
        overlay.Clear();
        Assert.Equal((7u, 2, 1), overlay.Get(path, true));
    }

    [Fact]
    public void DisposeReleasesTextureExactlyOnce()
    {
        _overlay.Get(WriteImage(), true);

        _overlay.Dispose();
        _overlay.Dispose();

        Assert.Equal(new uint[] { 1 }, _deleted);
    }

    public void Dispose()
    {
        _overlay.Dispose();
        Directory.Delete(_directory, true);
    }
}
