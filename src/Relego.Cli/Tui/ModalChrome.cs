using Terminal.Gui.Drawing;
using Terminal.Gui.ViewBase;
using Terminal.Gui.Views;

namespace Relego.Cli.Tui;

internal static class ModalChrome
{
    private const char BackdropShade = '\u2591';

    public static Label CreateBackdrop()
    {
        var backdrop = new Label
        {
            X = 0,
            Y = 0,
            Width = Dim.Fill(),
            Height = Dim.Fill(),
            Visible = false,
            CanFocus = false,
            Text = string.Empty
        };
        backdrop.SetScheme(CreateBackdropScheme());
        backdrop.ViewportChanged += (_, _) => RefreshBackdrop(backdrop);
        return backdrop;
    }

    public static void SetBackdropVisible(Label? backdrop, bool visible)
    {
        if (backdrop is null)
        {
            return;
        }

        backdrop.Visible = visible;
        if (visible)
        {
            RefreshBackdrop(backdrop);
        }
    }

    public static FrameView CreateFrame(int width, int height, string? title = null)
    {
        var frame = new FrameView
        {
            X = Pos.Center(),
            Y = Pos.Center(),
            Width = width,
            Height = height,
            Title = title ?? string.Empty,
            BorderStyle = LineStyle.Rounded,
            CanFocus = true,
            Visible = false
        };
        frame.SetScheme(CreateFrameScheme(isFocused: true));
        return frame;
    }

    public static Scheme CreateFrameScheme(bool isFocused)
    {
        var palette = TuiTheme.Palette;
        var borderColor = isFocused ? palette.BorderFocus : palette.Border;
        var attribute = new Terminal.Gui.Drawing.Attribute(borderColor, palette.Background);

        return new Scheme(attribute)
        {
            Normal = attribute,
            Focus = attribute,
            Active = attribute,
            HotNormal = attribute,
            HotFocus = attribute,
            HotActive = attribute,
            Disabled = attribute
        };
    }

    public static Scheme CreateBodyTextScheme()
    {
        var palette = TuiTheme.Palette;
        var attribute = new Terminal.Gui.Drawing.Attribute(palette.Text, palette.Background);
        return CreateUniformScheme(attribute);
    }

    public static Scheme CreateMutedTextScheme()
    {
        var palette = TuiTheme.Palette;
        var attribute = new Terminal.Gui.Drawing.Attribute(palette.TextMuted, palette.Background);
        return CreateUniformScheme(attribute);
    }

    public static Scheme CreateHintKeyScheme()
    {
        var palette = TuiTheme.Palette;
        var attribute = new Terminal.Gui.Drawing.Attribute(palette.AccentText, palette.Background);
        return CreateUniformScheme(attribute);
    }

    private static Scheme CreateBackdropScheme()
    {
        var palette = TuiTheme.Palette;
        var attribute = new Terminal.Gui.Drawing.Attribute(palette.Border, palette.Background);
        return CreateUniformScheme(attribute);
    }

    private static Scheme CreateUniformScheme(Terminal.Gui.Drawing.Attribute attribute) => new(attribute)
    {
        Normal = attribute,
        Focus = attribute,
        Active = attribute,
        HotNormal = attribute,
        HotFocus = attribute,
        HotActive = attribute,
        Disabled = attribute,
        ReadOnly = attribute,
        Editable = attribute,
        Highlight = attribute,
        Code = attribute
    };

    private static void RefreshBackdrop(Label backdrop)
    {
        var width = Math.Max(1, backdrop.Viewport.Width);
        var height = Math.Max(1, backdrop.Viewport.Height);
        var row = new string(BackdropShade, width);
        backdrop.Text = string.Join("\n", Enumerable.Repeat(row, height));
    }
}
