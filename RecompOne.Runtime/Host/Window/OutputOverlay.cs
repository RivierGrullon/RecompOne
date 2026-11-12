using StbImageSharp;

namespace RecompOne.Runtime.Host.Window;

// Owns one GPU texture; all operations run on the window's render thread.
internal sealed class OutputOverlay(
    Func<byte[], int, int, uint> upload,
    Action<uint> delete) : IDisposable
{
    private string? _path;
    private bool _attempted;
    private (uint Tex, int W, int H) _image;

    public string Error { get; private set; } = "";

    public (uint Tex, int W, int H) Get(string path, bool enabled)
    {
        if (!enabled || string.IsNullOrWhiteSpace(path))
        {
            Clear();
            return default;
        }

        if (_path != path)
        {
            Clear();
            _path = path;
        }

        if (_attempted) return _image;
        _attempted = true;

        try
        {
            var image = ImageResult.FromMemory(File.ReadAllBytes(path), ColorComponents.RedGreenBlueAlpha);
            if (image.Width <= 0 || image.Height <= 0)
                throw new InvalidDataException("Invalid image dimensions.");

            var texture = upload(image.Data, image.Width, image.Height);
            if (texture == 0) throw new InvalidOperationException("Could not upload overlay texture.");
            _image = (texture, image.Width, image.Height);
        }
        catch (Exception e)
        {
            Error = e.Message;
            Console.Error.WriteLine($"[Overlay] cannot load {path}: {e.Message}");
        }

        return _image;
    }

    public void Clear()
    {
        if (_image.Tex != 0) delete(_image.Tex);
        _image = default;
        _path = null;
        _attempted = false;
        Error = "";
    }

    public void Dispose() => Clear();
}
