using Dalamud.Bindings.ImGui;
using Dalamud.Interface;
using System;
using System.Numerics;

namespace SealBreaker.Windows;

/// <summary>Central palette and ImGui style helpers for the SealBreaker window.</summary>
internal static class UiTheme
{
    // ── Palette — SealBreaker identity: gold seal, teal crack, deep navy (icon.png) ──
    public static readonly Vector4 Accent     = new(1.00f, 0.78f, 0.35f, 1f);
    public static readonly Vector4 AccentDim  = new(1.00f, 0.78f, 0.35f, 0.55f);
    public static readonly Vector4 Teal       = new(0.08f, 0.87f, 0.84f, 1f);
    public static readonly Vector4 TealWash   = new(0.08f, 0.87f, 0.84f, 0.08f);
    public static readonly Vector4 Green      = new(0.30f, 0.76f, 0.43f, 1f);
    public static readonly Vector4 GreenDark  = new(0.20f, 0.40f, 0.27f, 1f);
    public static readonly Vector4 Red        = new(0.89f, 0.32f, 0.32f, 1f);
    public static readonly Vector4 RedDark    = new(0.46f, 0.18f, 0.18f, 1f);
    public static readonly Vector4 Yellow     = new(0.90f, 0.74f, 0.35f, 1f);
    public static readonly Vector4 Gray       = new(0.56f, 0.58f, 0.66f, 1f);
    public static readonly Vector4 TextBright = new(0.90f, 0.89f, 0.86f, 1f);

    public static readonly Vector4 WindowBg   = new(0.063f, 0.082f, 0.169f, 1f); // #10152B
    public static readonly Vector4 NavBg      = new(0.071f, 0.094f, 0.204f, 1f); // #121834
    public static readonly Vector4 ContentBg  = new(0.075f, 0.102f, 0.200f, 1f); // #131A33
    public static readonly Vector4 PanelBg    = new(0.094f, 0.122f, 0.227f, 1f); // #181F3A
    public static readonly Vector4 FrameBgCol = new(0.106f, 0.137f, 0.259f, 1f); // #1B2342
    public static readonly Vector4 NavyBorder = new(0.165f, 0.200f, 0.322f, 1f); // #2A3352
    public static readonly Vector4 NavHeader  = new(0.365f, 0.396f, 0.502f, 1f); // #5D6580
    public static readonly Vector4 NavText    = new(0.663f, 0.651f, 0.596f, 1f); // #A9A698

    public static readonly Vector4 CardBg     = new(0.094f, 0.122f, 0.227f, 1f); // panel navy
    public static readonly Vector4 CardBorder = new(0.165f, 0.200f, 0.322f, 1f);
    public static readonly Vector4 ErrorBg    = new(0.30f, 0.12f, 0.12f, 1f);

    // ── Window style scope ────────────────────────────────────
    public readonly struct StyleScope(int colors, int vars) : IDisposable
    {
        public void Dispose()
        {
            ImGui.PopStyleColor(colors);
            ImGui.PopStyleVar(vars);
        }
    }

    /// <summary>Push window-wide rounding/spacing/accent styles. Dispose at end of Draw.</summary>
    public static StyleScope Begin()
    {
        var vars = 0;
        var colors = 0;

        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 4f); vars++;
        ImGui.PushStyleVar(ImGuiStyleVar.ChildRounding, 6f); vars++;
        ImGui.PushStyleVar(ImGuiStyleVar.GrabRounding, 4f); vars++;
        ImGui.PushStyleVar(ImGuiStyleVar.PopupRounding, 4f); vars++;
        ImGui.PushStyleVar(ImGuiStyleVar.FramePadding, new Vector2(8, 5)); vars++;
        ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(8, 6)); vars++;

        ImGui.PushStyleColor(ImGuiCol.CheckMark, Accent); colors++;
        ImGui.PushStyleColor(ImGuiCol.SliderGrab, AccentDim); colors++;
        ImGui.PushStyleColor(ImGuiCol.SliderGrabActive, Accent); colors++;

        ImGui.PushStyleColor(ImGuiCol.WindowBg, WindowBg); colors++;
        ImGui.PushStyleColor(ImGuiCol.PopupBg, PanelBg); colors++;
        ImGui.PushStyleColor(ImGuiCol.Border, NavyBorder); colors++;
        ImGui.PushStyleColor(ImGuiCol.TitleBg, NavBg); colors++;
        ImGui.PushStyleColor(ImGuiCol.TitleBgActive, PanelBg); colors++;
        ImGui.PushStyleColor(ImGuiCol.FrameBg, FrameBgCol); colors++;
        ImGui.PushStyleColor(ImGuiCol.FrameBgHovered, Scale(FrameBgCol, 1.35f)); colors++;
        ImGui.PushStyleColor(ImGuiCol.FrameBgActive, Scale(FrameBgCol, 1.6f)); colors++;
        ImGui.PushStyleColor(ImGuiCol.Header, FrameBgCol); colors++;
        ImGui.PushStyleColor(ImGuiCol.HeaderHovered, Scale(FrameBgCol, 1.35f)); colors++;
        ImGui.PushStyleColor(ImGuiCol.HeaderActive, Scale(FrameBgCol, 1.6f)); colors++;
        ImGui.PushStyleColor(ImGuiCol.Button, FrameBgCol); colors++;
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Scale(FrameBgCol, 1.35f)); colors++;
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Scale(FrameBgCol, 1.6f)); colors++;
        ImGui.PushStyleColor(ImGuiCol.Tab, NavBg); colors++;
        ImGui.PushStyleColor(ImGuiCol.TabHovered, Scale(FrameBgCol, 1.35f)); colors++;
        ImGui.PushStyleColor(ImGuiCol.TabActive, FrameBgCol); colors++;

        return new StyleScope(colors, vars);
    }

    // ── Card ──────────────────────────────────────────────────
    private static readonly Vector2 CardPadding = new(10, 8);

    public readonly struct CardScope : IDisposable
    {
        private readonly Vector2 _topLeftScreen;
        private readonly float _fullWidth;

        internal CardScope(Vector2 topLeftScreen, float fullWidth)
        {
            _topLeftScreen = topLeftScreen;
            _fullWidth = fullWidth;
        }

        public void Dispose()
        {
            ImGui.PopTextWrapPos();
            ImGui.EndGroup();

            var contentMax = ImGui.GetItemRectMax();
            var min = _topLeftScreen;
            var max = new Vector2(min.X + _fullWidth, contentMax.Y + CardPadding.Y);

            var drawList = ImGui.GetWindowDrawList();
            drawList.ChannelsSetCurrent(0);
            drawList.AddRectFilled(min, max, ImGui.ColorConvertFloat4ToU32(CardBg), 6f);
            drawList.AddRect(min, max, ImGui.ColorConvertFloat4ToU32(CardBorder), 6f);
            drawList.ChannelsMerge();

            ImGui.Dummy(new Vector2(0, CardPadding.Y));
        }
    }

    /// <summary>Bordered, rounded, auto-height panel drawn behind its content. Dispose to close.</summary>
    public static CardScope Card()
    {
        var fullWidth = ImGui.GetContentRegionAvail().X;
        var topLeftScreen = ImGui.GetCursorScreenPos();

        var drawList = ImGui.GetWindowDrawList();
        drawList.ChannelsSplit(2);
        drawList.ChannelsSetCurrent(1);

        ImGui.SetCursorScreenPos(topLeftScreen + CardPadding);
        ImGui.BeginGroup();
        ImGui.PushTextWrapPos(ImGui.GetCursorPosX() + fullWidth - CardPadding.X * 2);
        return new CardScope(topLeftScreen, fullWidth);
    }

    // ── Text helpers ──────────────────────────────────────────

    /// <summary>Thin horizontal rule fading from gold into the navy border color.</summary>
    public static void GoldFadeRule()
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var gold = ImGui.ColorConvertFloat4ToU32(new Vector4(Accent.X, Accent.Y, Accent.Z, 0.9f));
        var fade = ImGui.ColorConvertFloat4ToU32(NavyBorder);
        ImGui.GetWindowDrawList().AddRectFilledMultiColor(
            pos, new Vector2(pos.X + width, pos.Y + 1f), gold, fade, fade, gold);
        ImGui.Dummy(new Vector2(0, 4));
    }

    public static void SectionTitle(string text)
    {
        ImGui.TextColored(Accent, text);
        var min = ImGui.GetItemRectMin();
        var max = ImGui.GetItemRectMax();
        ImGui.GetWindowDrawList().AddLine(
            new Vector2(min.X, max.Y + 2),
            new Vector2(min.X + ImGui.GetContentRegionAvail().X + (max.X - min.X), max.Y + 2),
            ImGui.ColorConvertFloat4ToU32(CardBorder));
        ImGui.Spacing();
    }

    public static void Icon(FontAwesomeIcon icon, Vector4 color)
    {
        ImGui.PushFont(UiBuilder.IconFont);
        ImGui.TextColored(color, icon.ToIconString());
        ImGui.PopFont();
    }

    /// <summary>Inline icon + short text, e.g. plugin status chips.</summary>
    public static void Chip(FontAwesomeIcon icon, string text, Vector4 color)
    {
        Icon(icon, color);
        ImGui.SameLine(0, 4);
        ImGui.TextColored(color, text);
    }

    public static void StatusDot(Vector4 color)
    {
        var size = ImGui.GetTextLineHeight();
        var pos = ImGui.GetCursorScreenPos();
        var center = new Vector2(pos.X + size * 0.45f, pos.Y + size * 0.58f);
        ImGui.GetWindowDrawList().AddCircleFilled(center, size * 0.28f, ImGui.ColorConvertFloat4ToU32(color));
        ImGui.Dummy(new Vector2(size * 0.9f, size));
    }

    public static void RightAlignedText(string text, Vector4 color)
    {
        var width = ImGui.CalcTextSize(text).X;
        var avail = ImGui.GetContentRegionAvail().X;
        if (avail > width)
            ImGui.SetCursorPosX(ImGui.GetCursorPosX() + avail - width);
        ImGui.TextColored(color, text);
    }

    // ── Banner shell pieces (header pill, stat tiles, panels, gauge) ──
    public static readonly Vector4 GaugeBg = new(0.047f, 0.063f, 0.141f, 1f); // #0C1024

    private static uint U32(Vector4 c) => ImGui.ColorConvertFloat4ToU32(c);

    public static Vector2 PillSize(string text)
    {
        var textSize = ImGui.CalcTextSize(text);
        return new Vector2(textSize.X + 30f, textSize.Y + 8f);
    }

    /// <summary>Rounded status pill (dot + label) tinted by the state colour, at a screen position.</summary>
    public static void PillAt(Vector2 pos, string text, Vector4 color)
    {
        var size = PillSize(text);
        var max = pos + size;
        var drawList = ImGui.GetWindowDrawList();
        drawList.AddRectFilled(pos, max, U32(color with { W = 0.16f }), size.Y / 2f);
        drawList.AddRect(pos, max, U32(color with { W = 0.55f }), size.Y / 2f);
        drawList.AddCircleFilled(new Vector2(pos.X + 11f, pos.Y + size.Y / 2f), 3f, U32(color));
        drawList.AddText(new Vector2(pos.X + 20f, pos.Y + 4f), U32(color), text);
    }

    public static float StatTileHeight => MathF.Round(ImGui.GetFontSize() * 2.07f + 23f);

    /// <summary>One stat tile: small caps label, large value, optional dim sub-value, coloured
    /// left stripe. Returns true while hovered so the caller can attach a tooltip.</summary>
    public static bool StatTile(string label, string value, string? sub, float width, Vector4 stripe, Vector4? valueColor = null)
    {
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(width, StatTileHeight);
        var max = pos + size;
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(pos, max, U32(CardBg), 6f);
        drawList.AddRect(pos, max, U32(CardBorder), 6f);
        drawList.AddRectFilled(pos, new Vector2(pos.X + 3f, max.Y), U32(stripe), 6f, ImDrawFlags.RoundCornersLeft);

        var font = ImGui.GetFont();
        var baseSize = ImGui.GetFontSize();
        var smallSize = baseSize * 0.82f;
        var valueSize = baseSize * 1.25f;

        drawList.AddText(font, smallSize, new Vector2(pos.X + 11f, pos.Y + 8f), U32(Gray), label.ToUpperInvariant());

        var valuePos = new Vector2(pos.X + 11f, max.Y - 9f - valueSize);
        drawList.AddText(font, valueSize, valuePos, U32(valueColor ?? TextBright), value);

        if (!string.IsNullOrEmpty(sub))
        {
            var valueWidth = ImGui.CalcTextSize(value).X * 1.25f;
            drawList.AddText(font, smallSize,
                new Vector2(valuePos.X + valueWidth + 6f, max.Y - 10f - smallSize), U32(Gray), sub);
        }

        ImGui.Dummy(size);
        return ImGui.IsItemHovered();
    }

    /// <summary>Fixed-size card as a child window — use where cards sit side by side
    /// (<see cref="Card"/> splits draw channels and cannot share a row or live in a table).</summary>
    public static bool BeginPanel(string id, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.ChildBg, CardBg);
        ImGui.PushStyleColor(ImGuiCol.Border, CardBorder);
        ImGui.PushStyleVar(ImGuiStyleVar.WindowPadding, new Vector2(11, 9));
        return ImGui.BeginChild(id, size, true, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse);
    }

    public static void EndPanel()
    {
        ImGui.EndChild();
        ImGui.PopStyleVar();
        ImGui.PopStyleColor(2);
    }

    /// <summary>Small caps panel title with an optional right-aligned note.</summary>
    public static void PanelTitle(string label, string? right = null, Vector4? rightColor = null)
    {
        var font = ImGui.GetFont();
        var smallSize = ImGui.GetFontSize() * 0.82f;
        var pos = ImGui.GetCursorScreenPos();
        var avail = ImGui.GetContentRegionAvail().X;
        var drawList = ImGui.GetWindowDrawList();
        var caps = label.ToUpperInvariant();

        drawList.AddText(font, smallSize, new Vector2(pos.X, pos.Y + 1f), U32(Gray), caps);

        if (!string.IsNullOrEmpty(right))
        {
            var labelWidth = ImGui.CalcTextSize(caps).X * 0.82f;
            var shown = Ellipsize(right, avail - labelWidth - 14f);
            var width = ImGui.CalcTextSize(shown).X;
            drawList.AddText(new Vector2(pos.X + avail - width, pos.Y - 1f), U32(rightColor ?? TextBright), shown);
        }

        ImGui.Dummy(new Vector2(0, ImGui.GetFontSize() + 3f));
    }

    /// <summary>Thin rounded progress gauge filling the available width.</summary>
    public static void Gauge(float fraction, Vector4 from, Vector4 to, float height = 8f)
    {
        var pos = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X;
        var max = new Vector2(pos.X + width, pos.Y + height);
        var drawList = ImGui.GetWindowDrawList();

        drawList.AddRectFilled(pos, max, U32(GaugeBg), height / 2f);

        var filled = Math.Clamp(fraction, 0f, 1f) * (width - 2f);
        if (filled >= 1f)
        {
            drawList.AddRectFilledMultiColor(
                new Vector2(pos.X + 1f, pos.Y + 1f), new Vector2(pos.X + 1f + filled, max.Y - 1f),
                U32(from), U32(to), U32(to), U32(from));
        }

        drawList.AddRect(pos, max, U32(CardBorder), height / 2f);
        ImGui.Dummy(new Vector2(width, height));
    }

    /// <summary>Cuts text to fit a pixel width, ending in "..." when it had to be cut.</summary>
    public static string Ellipsize(string text, float maxWidth)
    {
        if (maxWidth <= 0f || string.IsNullOrEmpty(text) || ImGui.CalcTextSize(text).X <= maxWidth)
            return text;

        const string dots = "...";
        var low = 0;
        var high = text.Length;
        while (low < high)
        {
            var mid = (low + high + 1) / 2;
            if (ImGui.CalcTextSize(text[..mid] + dots).X <= maxWidth)
                low = mid;
            else
                high = mid - 1;
        }

        return low == 0 ? dots : text[..low].TrimEnd() + dots;
    }

    // ── Buttons ───────────────────────────────────────────────
    public static bool SolidButton(string label, Vector4 baseColor, Vector2 size)
    {
        ImGui.PushStyleColor(ImGuiCol.Button, baseColor);
        ImGui.PushStyleColor(ImGuiCol.ButtonHovered, Scale(baseColor, 1.25f));
        ImGui.PushStyleColor(ImGuiCol.ButtonActive, Scale(baseColor, 0.85f));
        ImGui.PushStyleColor(ImGuiCol.Text, TextBright);
        var clicked = ImGui.Button(label, size);
        ImGui.PopStyleColor(4);
        return clicked;
    }

    public static readonly Vector4 YellowDark = new(0.42f, 0.34f, 0.12f, 1f);

    public static bool StartButton(string label, Vector2 size) => SolidButton(label, GreenDark, size);
    public static bool StopButton(string label, Vector2 size) => SolidButton(label, RedDark, size);

    private static Vector4 Scale(Vector4 c, float f) =>
        new(Math.Clamp(c.X * f, 0f, 1f), Math.Clamp(c.Y * f, 0f, 1f), Math.Clamp(c.Z * f, 0f, 1f), c.W);
}
