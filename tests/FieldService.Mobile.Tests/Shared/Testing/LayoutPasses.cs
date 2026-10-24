using Microsoft.Maui;
using Microsoft.Maui.Controls;
using Microsoft.Maui.Graphics;
using NSubstitute;

namespace FieldService.Mobile.Tests.Shared.Testing;

public sealed class LayoutPasses
{
    private const double Tolerance = 0.0005;

    private readonly ICrossPlatformLayout _cross;
    private readonly ILayoutHandler _handler;

    public LayoutPasses(Layout layout)
    {
        ArgumentNullException.ThrowIfNull(layout);
        _handler = Substitute.For<ILayoutHandler>();
        layout.Handler = _handler;
        _handler.ClearReceivedCalls();
        _cross = layout;
    }

    public int Measures { get; private set; }

    public Size Measured { get; private set; }

    public int Invalidations => _handler.ReceivedCalls().Count(call => call.GetArguments() is [string command, ..] && command == nameof(IView.InvalidateMeasure));

    public Size Measure(double width)
    {
        Measures++;
        Measured = _cross.CrossPlatformMeasure(width, double.PositiveInfinity);
        return Measured;
    }

    public Rect Arrange(double width)
    {
        var bounds = new Rect(0, 0, width, Measured.Height);
        _cross.CrossPlatformArrange(bounds);
        return bounds;
    }

    public IReadOnlyList<bool> RunParent(double measureWidth, double arrangeWidth, int rounds, Func<bool> mode)
    {
        ArgumentNullException.ThrowIfNull(mode);

        var modes = new List<bool>();
        var seen = Invalidations;
        Measure(measureWidth);
        modes.Add(mode());
        for (var round = 0; round < rounds; round++)
        {
            Arrange(arrangeWidth);
            modes.Add(mode());
            if (Invalidations > seen)
            {
                seen = Invalidations;
                Measure(measureWidth);
                modes.Add(mode());
            }
        }

        return modes;
    }

    public static void Inside(Rect bounds, Rect frame, string name) =>
        Assert.True(
            frame.Left >= bounds.Left - Tolerance && frame.Top >= bounds.Top - Tolerance && frame.Right <= bounds.Right + Tolerance && frame.Bottom <= bounds.Bottom + Tolerance,
            $"{name} {frame} lies outside {bounds}");
}
