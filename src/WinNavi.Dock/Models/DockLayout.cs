using System.Windows;
using System.Windows.Controls;

namespace WinNavi.Dock.Models;

// Everything in the dock UI that depends on which screen edge it sits on, or on the icon size. Bound from XAML.
public sealed record DockLayout(
    Orientation Orientation,
    double ItemSize,              // icon edge length in DIPs
    Thickness ItemMargin,
    Point Origin,                 // zoom origin: the side facing the screen edge
    HorizontalAlignment DotH,
    VerticalAlignment DotV,
    Thickness DotMargin,
    double DotSize,
    double SepWidth, double SepHeight,
    double LineWidth, double LineHeight,
    Thickness GearMargin)
{
    public const double MinIconSize = 16;
    public const double MaxIconSize = 128;
    public const double DefaultIconSize = 32;

    // Length of a separator slot along the dock's main axis.
    public const double SepSlot = 16;

    public static double ClampSize(double size) => Math.Clamp(size, MinIconSize, MaxIconSize);

    public static DockLayout For(DockEdge edge, double iconSize)
    {
        double s = ClampSize(iconSize);
        double line = s * 0.7; // separator line length, across the dock

        // Running badge size scales with the icon: 16px icon -> 6px badge, 128px icon -> 13px badge.
        double dot = 6 + (s - MinIconSize) / (MaxIconSize - MinIconSize) * 7;
        double off = -dot / 2; // half the badge sits outside the icon's edge-facing side

        return edge switch
        {
            DockEdge.Top => new(Orientation.Horizontal, s, new(6, 0, 6, 0), new(0.5, 0),
                HorizontalAlignment.Center, VerticalAlignment.Top, new(0, off, 0, 0), dot,
                SepSlot, s, 2, line, new(6, 0, 0, 0)),
            DockEdge.Left => new(Orientation.Vertical, s, new(0, 6, 0, 6), new(0, 0.5),
                HorizontalAlignment.Left, VerticalAlignment.Center, new(off, 0, 0, 0), dot,
                s, SepSlot, line, 2, new(0, 6, 0, 0)),
            DockEdge.Right => new(Orientation.Vertical, s, new(0, 6, 0, 6), new(1, 0.5),
                HorizontalAlignment.Right, VerticalAlignment.Center, new(0, 0, off, 0), dot,
                s, SepSlot, line, 2, new(0, 6, 0, 0)),
            _ => new(Orientation.Horizontal, s, new(6, 0, 6, 0), new(0.5, 1),
                HorizontalAlignment.Center, VerticalAlignment.Bottom, new(0, 0, 0, off), dot,
                SepSlot, s, 2, line, new(6, 0, 0, 0)),
        };
    }
}
