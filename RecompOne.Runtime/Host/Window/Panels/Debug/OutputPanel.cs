using System.Numerics;
using ImGuiNET;
using RecompOne.Runtime.Config;

namespace RecompOne.Runtime.Host.Window;

internal sealed class OutputPanel(OutputOverlay overlay) : IPanel, IDisposable
{
    private bool _reloadOverlay;

    public OutputOverlay Overlay => overlay;

    // Settings are drawn after this panel. Keep its queued texture alive until
    // the next frame, when no draw commands can still reference it.
    public void ReloadOverlay() => _reloadOverlay = true;

    public string Name => "Output";
    public string TitleKey => "panel.output";

    public bool IsOpen
    {
        get => true;
        set { }
    }

    private static uint _texId;
    private static int _texW, _texH;
    private static float _aspect = 4f / 3f;

    public static bool IsDocked { get; private set; }

    public static int ViewWidth { get; private set; }
    public static int ViewHeight { get; private set; }

    public static void SetTexture(uint id, int w, int h, float aspect = 0f)
    {
        (_texId, _texW, _texH, _aspect) = (id, w, h, aspect > 0f ? aspect : 4f / 3f);
    }

    public void Draw()
    {
        ImGui.SetNextWindowSize(new Vector2(640, 480), ImGuiCond.FirstUseEver);
        ImGui.PushStyleColor(ImGuiCol.WindowBg, new Vector4(0f, 0f, 0f, 1f));

        var visible = ImGui.Begin(this.Title());
        IsDocked = ImGui.IsWindowDocked();
        if (_reloadOverlay)
        {
            overlay.Clear();
            _reloadOverlay = false;
        }
        var artwork = overlay.Get(DisplayOptions.OverlayPath, DisplayOptions.OverlayEnabled);

        if (!visible)
        {
            ImGui.End();
            ImGui.PopStyleColor();
            return;
        }

        var avail = ImGui.GetContentRegionAvail();
        if (artwork.Tex != 0 && avail.X > 0f && avail.Y > 0f)
        {
            var origin = ImGui.GetCursorScreenPos();
            var size = FitAspect(new Vector2(artwork.W, artwork.H), avail);
            var min = origin + (avail - size) * 0.5f;
            var draw = ImGui.GetWindowDrawList();
            draw.PushClipRect(origin, origin + avail, true);
            draw.AddImage((nint)artwork.Tex, min, min + size);
            draw.PopClipRect();
        }

        if (_texId != 0 && _texW > 0 && _texH > 0 && avail.X > 0f && avail.Y > 0f)
        {
            var imageSize = FitAspect(new Vector2(_aspect, 1f), avail);
            var scale = ImGui.GetIO().DisplayFramebufferScale;
            ViewWidth = (int)MathF.Round(imageSize.X * scale.X);
            ViewHeight = (int)MathF.Round(imageSize.Y * scale.Y);
            var offset = (avail - imageSize) * 0.5f;
            ImGui.SetCursorPos(ImGui.GetCursorPos() + offset);
            ImGui.Image((nint)_texId, imageSize);
        }

        ToastNotifications.Draw();

        ImGui.End();
        ImGui.PopStyleColor();
    }

    public void Dispose() => overlay.Dispose();

    private static Vector2 FitAspect(Vector2 src, Vector2 dst)
    {
        var scale = MathF.Min(dst.X / src.X, dst.Y / src.Y);
        return src * scale;
    }
}
