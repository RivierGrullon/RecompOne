using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Config;
using RecompOne.Runtime.Host.Window;
using Xunit;

[assembly: CollectionBehavior(DisableTestParallelization = true)]

namespace RecompOne.Runtime.Tests;

// Inspect real ImGui draw data without a GPU or a game disc.
public sealed class OutputPanelTests : IDisposable
{
    const uint OverlayTexture = 101;
    const uint GameTexture = 202;
    readonly string _directory = Path.Combine(Path.GetTempPath(), "recomp-panel-" + Guid.NewGuid());
    readonly string _workingDirectory = Environment.CurrentDirectory;
    readonly nint _context;
    readonly OutputPanel _panel;
    readonly OutputOverlay _overlay;
    readonly List<uint> _deleted = [];

    public unsafe OutputPanelTests()
    {
        Directory.CreateDirectory(_directory);
        Environment.CurrentDirectory = _directory;
        _context = ImGui.CreateContext();
        var io = ImGui.GetIO();
        io.NativePtr->IniFilename = null;
        io.DisplaySize = new Vector2(2000, 2000);
        io.DisplayFramebufferScale = Vector2.One;
        io.DeltaTime = 1f / 60f;
        io.Fonts.AddFontDefault();
        io.Fonts.GetTexDataAsRGBA32(out nint _, out _, out _, out _);
        io.Fonts.SetTexID(1);
        var style = ImGui.GetStyle();
        style.WindowPadding = Vector2.Zero;
        style.WindowBorderSize = 0;
        style.FramePadding = Vector2.Zero;
        Localization.Load();
        ToastNotifications.Clear();

        _overlay = new OutputOverlay((_, _, _) => OverlayTexture, _deleted.Add);
        _panel = new OutputPanel(_overlay);
        OutputPanel.SetTexture(GameTexture, 320, 240);
        DisplayOptions.OverlayEnabled.Value = false;
        DisplayOptions.OverlayPath.Value = "";

        ImGui.NewFrame();
        _panel.Draw();
        ImGui.Render();
    }

    void EnableImage()
    {
        var path = Path.Combine(_directory, "bezel.png");
        File.WriteAllBytes(path, Convert.FromBase64String(OutputOverlayTests.Png));
        DisplayOptions.OverlayPath.Value = path;
        DisplayOptions.OverlayEnabled.Value = true;
    }

    // Returns each textured quad in draw order, with its actual clip rectangle.
    List<(uint Texture, Vector2 Min, Vector2 Max, Vector4 Clip)> Frame(float width, float height)
    {
        ImGui.NewFrame();
        ImGui.SetWindowPos(_panel.Title(), new Vector2(10, 20));
        ImGui.SetWindowSize(_panel.Title(), new Vector2(width, height + ImGui.GetFontSize()));
        _panel.Draw();
        ImGui.Render();

        var result = new List<(uint, Vector2, Vector2, Vector4)>();
        var data = ImGui.GetDrawData();
        for (var i = 0; i < data.CmdListsCount; i++)
        {
            var list = data.CmdLists[i];
            for (var j = 0; j < list.CmdBuffer.Size; j++)
            {
                var command = list.CmdBuffer[j];
                var texture = (uint)command.TextureId;
                if (texture != OverlayTexture && texture != GameTexture) continue;
                var start = (int)(command.VtxOffset + list.IdxBuffer[(int)command.IdxOffset]);
                result.Add((texture, list.VtxBuffer[start].pos, list.VtxBuffer[start + 2].pos, command.ClipRect));
            }
        }
        return result;
    }

    [Theory]
    [InlineData(800, 600, 800, 400, 800, 600)]
    [InlineData(1200, 600, 1200, 600, 800, 600)]
    [InlineData(600, 900, 600, 300, 600, 450)]
    public void ArtworkIsCenteredAndDrawnBeforeGameWithoutChangingGameSize(
        float width, float height, float artworkWidth, float artworkHeight, float gameWidth, float gameHeight)
    {
        EnableImage();

        var quads = Frame(width, height);

        Assert.Equal(new uint[] { OverlayTexture, GameTexture }, quads.Select(q => q.Texture));
        Assert.Equal(new Vector2(artworkWidth, artworkHeight), quads[0].Max - quads[0].Min);
        Assert.Equal(new Vector2(gameWidth, gameHeight), quads[1].Max - quads[1].Min);
        Assert.Equal((quads[0].Min + quads[0].Max) / 2, (quads[1].Min + quads[1].Max) / 2);
        Assert.Equal((int)gameWidth, OutputPanel.ViewWidth);
        Assert.Equal((int)gameHeight, OutputPanel.ViewHeight);
        foreach (var quad in quads)
        {
            Assert.True(quad.Min.X >= quad.Clip.X && quad.Min.Y >= quad.Clip.Y);
            Assert.True(quad.Max.X <= quad.Clip.Z && quad.Max.Y <= quad.Clip.W);
        }
    }

    [Fact]
    public void ReloadKeepsQueuedTextureAliveUntilNextOutputFrame()
    {
        EnableImage();
        Frame(1200, 600);

        // Settings are drawn after the output panel, but before ImGui renders.
        _panel.ReloadOverlay();
        Assert.Empty(_deleted);

        Assert.Equal(new uint[] { OverlayTexture, GameTexture }, Frame(1200, 600).Select(q => q.Texture));
        Assert.Equal(new uint[] { OverlayTexture }, _deleted);
    }

    [Fact]
    public void DisabledOverlayDoesNotChangeGameplay()
    {
        EnableImage();
        Frame(1200, 600);
        DisplayOptions.OverlayEnabled.Value = false;

        var quad = Assert.Single(Frame(1200, 600));
        Assert.Equal(GameTexture, quad.Texture);
        Assert.Equal(new Vector2(800, 600), quad.Max - quad.Min);
    }

    [Fact]
    public void MissingImageLeavesGameplayVisible()
    {
        DisplayOptions.OverlayPath.Value = Path.Combine(_directory, "missing.png");
        DisplayOptions.OverlayEnabled.Value = true;

        var quad = Assert.Single(Frame(1200, 600));
        Assert.Equal(GameTexture, quad.Texture);
        Assert.Equal(new Vector2(800, 600), quad.Max - quad.Min);
        Assert.NotEmpty(_overlay.Error);
    }

    [Fact]
    public void HighDpiScalesRenderResolutionWithoutMovingArtwork()
    {
        EnableImage();
        ImGui.GetIO().DisplayFramebufferScale = new Vector2(2, 2);

        var quads = Frame(1200, 600);

        Assert.Equal(new Vector2(1200, 600), quads[0].Max - quads[0].Min);
        Assert.Equal(new Vector2(800, 600), quads[1].Max - quads[1].Min);
        Assert.Equal(1600, OutputPanel.ViewWidth);
        Assert.Equal(1200, OutputPanel.ViewHeight);
    }

    [Fact]
    public void WidescreenGameKeepsItsAspectRatio()
    {
        EnableImage();
        OutputPanel.SetTexture(GameTexture, 426, 240, 16f / 9f);

        var quad = Frame(1280, 720).Last();

        Assert.Equal(GameTexture, quad.Texture);
        Assert.Equal(new Vector2(1280, 720), quad.Max - quad.Min);
    }

    public void Dispose()
    {
        _overlay.Dispose();
        DisplayOptions.OverlayEnabled.Reset();
        DisplayOptions.OverlayPath.Reset();
        Settings.SaveIfDirty();
        OutputPanel.SetTexture(0, 0, 0);
        ImGui.DestroyContext(_context);
        Environment.CurrentDirectory = _workingDirectory;
        Directory.Delete(_directory, true);
    }
}
