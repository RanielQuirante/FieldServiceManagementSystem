using System.Runtime.CompilerServices;
using FieldService.Mobile.Shared.Controls;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using AutomationProperties = Microsoft.UI.Xaml.Automation.AutomationProperties;
using MauiThickness = Microsoft.Maui.Thickness;
using PlatformPoint = Windows.Foundation.Point;

namespace FieldService.Mobile.WinUI;

public static class ListItems
{
    public const string SelectionIndicatorKey = "ListViewItemSelectionIndicatorVisualEnabled";
    public const string EmptyViewPartName = "EmptyViewContentControl";

    public static readonly string[] ItemBrushKeys =
    [
        "ListViewItemBackground",
        "ListViewItemBackgroundPointerOver",
        "ListViewItemBackgroundPressed",
        "ListViewItemBackgroundSelected",
        "ListViewItemBackgroundSelectedPointerOver",
        "ListViewItemBackgroundSelectedPressed",
        "ListViewItemBackgroundSelectedDisabled",
        "ListViewItemSelectionIndicatorBrush",
        "ListViewItemSelectionIndicatorPointerOverBrush",
        "ListViewItemSelectionIndicatorPressedBrush",
        "ListViewItemSelectionIndicatorDisabledBrush",
    ];

    private static readonly ConditionalWeakTable<FrameworkElement, JobCard> _cards = new();
    private static readonly ConditionalWeakTable<TextBlock, Label> _headers = new();

    public static void AttachList(ListViewBase list, BindableObject view)
    {
        ArgumentNullException.ThrowIfNull(list);
        ArgumentNullException.ThrowIfNull(view);

        var clear = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        foreach (var key in ItemBrushKeys)
        {
            list.Resources[key] = clear;
        }

        list.Resources[SelectionIndicatorKey] = false;
        list.SingleSelectionFollowsFocus = false;
        if (ListSemantics.GetName(view) is { Length: > 0 } name)
        {
            AutomationProperties.SetName(list, name);
        }

        list.Loaded -= OnListLoaded;
        list.Loaded += OnListLoaded;
        if (list.IsLoaded)
        {
            KeepEmptyViewOutOfTabOrder(list);
        }
    }

    public static void KeepEmptyViewOutOfTabOrder(ListViewBase list)
    {
        ArgumentNullException.ThrowIfNull(list);

        if (Descendant<Control>(list, EmptyViewPartName) is { } emptyView)
        {
            emptyView.IsTabStop = false;
        }
    }

    private static void OnListLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is ListViewBase list)
        {
            KeepEmptyViewOutOfTabOrder(list);
        }
    }

    private static T? Descendant<T>(DependencyObject parent, string name)
        where T : FrameworkElement
    {
        var count = VisualTreeHelper.GetChildrenCount(parent);
        for (var index = 0; index < count; index++)
        {
            var child = VisualTreeHelper.GetChild(parent, index);
            if (child is T found && found.Name == name)
            {
                return found;
            }

            if (Descendant<T>(child, name) is { } nested)
            {
                return nested;
            }
        }

        return null;
    }

    public static void AttachCard(FrameworkElement card, JobCard view)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(view);

        if (!_cards.TryGetValue(card, out _))
        {
            _cards.Add(card, view);
            card.Loaded += OnCardChanged;
            card.SizeChanged += OnCardChanged;
        }

        ApplyCard(card, view);
    }

    public static void ApplyCard(FrameworkElement card, JobCard view)
    {
        ArgumentNullException.ThrowIfNull(card);
        ArgumentNullException.ThrowIfNull(view);

        if (Ancestor<ListViewItem>(card) is not { } item || view.IsLoading)
        {
            return;
        }

        AutomationProperties.SetName(item, view.Description ?? string.Empty);
        AutomationProperties.SetAccessibilityView(card, AccessibilityView.Raw);
        if (view.Parent is Microsoft.Maui.Controls.Layout layout && !FocusRing.IsEven(layout.Padding))
        {
            var (even, outside) = FocusRing.EvenInset(layout.Padding);
            layout.Padding = even;
            item.Margin = new Microsoft.UI.Xaml.Thickness(outside.Left, outside.Top, outside.Right, outside.Bottom);
            return;
        }

        if (card.ActualWidth <= 0 || item.ActualWidth <= 0)
        {
            return;
        }

        var origin = card.TransformToVisual(item).TransformPoint(new PlatformPoint(0, 0));
        var inset = new MauiThickness(
            origin.X,
            origin.Y,
            item.ActualWidth - origin.X - card.ActualWidth,
            item.ActualHeight - origin.Y - card.ActualHeight);
        var margin = FocusVisuals.MarginAround(inset);
        FocusVisuals.Apply(item, FocusRing.LightColorKey, margin);
        if (VisualTreeHelper.GetChildrenCount(item) > 0 && VisualTreeHelper.GetChild(item, 0) is Microsoft.UI.Xaml.Controls.ContentPresenter presenter)
        {
            presenter.CornerRadius = new Microsoft.UI.Xaml.CornerRadius(FocusRing.ContainerRadius(view.IsCompact ? JobCard.CompactRadius : JobCard.Radius, margin.Left));
        }
    }

    public static void AttachGroupHeader(TextBlock text, Label label)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(label);

        if (!_headers.TryGetValue(text, out _))
        {
            _headers.Add(text, label);
            text.Loaded += OnHeaderLoaded;
        }

        ApplyGroupHeader(text, label);
    }

    public static void ApplyGroupHeader(TextBlock text, Label label)
    {
        ArgumentNullException.ThrowIfNull(text);
        ArgumentNullException.ThrowIfNull(label);

        if (!ListSemantics.GetIsOwnStop(label) || Ancestor<ListViewHeaderItem>(text) is not { } header)
        {
            return;
        }

        AutomationProperties.SetName(header, text.Text ?? string.Empty);
        AutomationProperties.SetHeadingLevel(header, HeadingFor(SemanticProperties.GetHeadingLevel(label)));
        AutomationProperties.SetAccessibilityView(text, AccessibilityView.Raw);
        header.IsTabStop = false;
    }

    private static void OnCardChanged(object sender, object e)
    {
        if (sender is FrameworkElement card && _cards.TryGetValue(card, out var view))
        {
            ApplyCard(card, view);
        }
    }

    private static void OnHeaderLoaded(object sender, RoutedEventArgs e)
    {
        if (sender is TextBlock text && _headers.TryGetValue(text, out var label))
        {
            ApplyGroupHeader(text, label);
        }
    }

    private static AutomationHeadingLevel HeadingFor(SemanticHeadingLevel level) => level switch
    {
        SemanticHeadingLevel.Level1 => AutomationHeadingLevel.Level1,
        SemanticHeadingLevel.Level2 => AutomationHeadingLevel.Level2,
        SemanticHeadingLevel.Level3 => AutomationHeadingLevel.Level3,
        SemanticHeadingLevel.Level4 => AutomationHeadingLevel.Level4,
        SemanticHeadingLevel.Level5 => AutomationHeadingLevel.Level5,
        SemanticHeadingLevel.Level6 => AutomationHeadingLevel.Level6,
        SemanticHeadingLevel.Level7 => AutomationHeadingLevel.Level7,
        SemanticHeadingLevel.Level8 => AutomationHeadingLevel.Level8,
        SemanticHeadingLevel.Level9 => AutomationHeadingLevel.Level9,
        _ => AutomationHeadingLevel.None,
    };

    private static T? Ancestor<T>(DependencyObject start)
        where T : DependencyObject
    {
        for (var node = VisualTreeHelper.GetParent(start); node is not null; node = VisualTreeHelper.GetParent(node))
        {
            if (node is T found)
            {
                return found;
            }
        }

        return null;
    }
}
