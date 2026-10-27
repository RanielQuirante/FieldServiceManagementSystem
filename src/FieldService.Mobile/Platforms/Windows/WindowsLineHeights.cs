using System.Runtime.CompilerServices;
using FieldService.Mobile.Shared.Controls;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.UI.ViewManagement;

namespace FieldService.Mobile.WinUI;

public static class WindowsLineHeights
{
    private static readonly ConditionalWeakTable<TextBlock, Wanted> _labels = new();
    private static readonly Dictionary<(string Family, double Size, ushort Weight, bool Scales, double Scale), double> _natural = [];
    private static UISettings? _settings;
    private static double _scale = 1;

    public static void Apply(TextBlock text, double themeLineHeight, double spec)
    {
        ArgumentNullException.ThrowIfNull(text);

        if (themeLineHeight <= 0)
        {
            _labels.Remove(text);
            return;
        }

        WatchTextScale();
        var wanted = new Wanted(themeLineHeight, spec);
        _labels.AddOrUpdate(text, wanted);
        Update(text, wanted);
    }

    private static void Update(TextBlock text, Wanted wanted)
    {
        var lineHeight = LineHeights.WindowsLineHeight(wanted.ThemeLineHeight, wanted.Spec, Natural(text));
        if (lineHeight <= 0)
        {
            return;
        }

        text.LineStackingStrategy = LineStackingStrategy.BlockLineHeight;
        text.LineHeight = lineHeight;
    }

    private static double Natural(TextBlock text)
    {
        var key = (text.FontFamily?.Source ?? string.Empty, text.FontSize, text.FontWeight.Weight, text.IsTextScaleFactorEnabled, _scale);
        if (_natural.TryGetValue(key, out var height))
        {
            return height;
        }

        var probe = new TextBlock
        {
            Text = "A",
            FontSize = text.FontSize,
            FontWeight = text.FontWeight,
            FontStyle = text.FontStyle,
            IsTextScaleFactorEnabled = text.IsTextScaleFactorEnabled,
            UseLayoutRounding = false,
        };
        if (text.FontFamily is { } family)
        {
            probe.FontFamily = family;
        }

        probe.Measure(new Windows.Foundation.Size(double.PositiveInfinity, double.PositiveInfinity));
        height = probe.DesiredSize.Height;
        _natural[key] = height;
        return height;
    }

    private static void WatchTextScale()
    {
        if (_settings is not null)
        {
            return;
        }

        var queue = DispatcherQueue.GetForCurrentThread();
        _settings = new UISettings();
        _scale = _settings.TextScaleFactor;
        _settings.TextScaleFactorChanged += (sender, _) => queue?.TryEnqueue(() =>
        {
            _scale = sender.TextScaleFactor;
            _natural.Clear();
            foreach (var (label, wanted) in _labels)
            {
                Update(label, wanted);
            }
        });
    }

    private sealed record Wanted(double ThemeLineHeight, double Spec);
}
