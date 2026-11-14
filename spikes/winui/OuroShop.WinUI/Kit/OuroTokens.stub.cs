// Stand-in for ouro-ui-core's dist/windows/OuroTokens.cs until a core release carries it: the Pearl theme's values,
// copied from core v0.10.0's OuroTokens.swift. Replace with the vendored file; never grow this by hand.
using Windows.UI;

namespace Ouro.UI.WinUI;

public sealed record OuroColors(Color Primary, Color PrimaryForeground, Color Secondary, Color SecondaryForeground);

public static class OuroTokens
{
    public static readonly OuroColors Light = new(Color.FromArgb(0xFF, 0x00, 0x66, 0xCC), Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF), Color.FromArgb(0xFF, 0xF2, 0xF2, 0xF5), Color.FromArgb(0xFF, 0x1D, 0x1D, 0x1F));
    public static readonly OuroColors Dark = new(Color.FromArgb(0xFF, 0x29, 0x97, 0xFF), Color.FromArgb(0xFF, 0x00, 0x17, 0x2E), Color.FromArgb(0xFF, 0x2C, 0x2C, 0x2E), Color.FromArgb(0xFF, 0xF5, 0xF5, 0xF7));
    public const double Radius = 18;
    public const float PressScale = 0.97f;
    public const int PressMillis = 160;
}
