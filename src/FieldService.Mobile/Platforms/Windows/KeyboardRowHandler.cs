using FieldService.Mobile.Shared.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Windows.System;
using PlatformSize = Windows.Foundation.Size;

namespace FieldService.Mobile.WinUI;

public sealed partial class KeyboardRowHandler : ViewHandler<IContentView, KeyboardRowHost>
{
    public static readonly IPropertyMapper<IContentView, KeyboardRowHandler> RowMapper =
        new PropertyMapper<IContentView, KeyboardRowHandler>(ViewMapper)
        {
            [nameof(IContentView.Content)] = MapContent,
            [nameof(IView.IsEnabled)] = MapEnabled,
            [nameof(OptionRow.IsSelected)] = MapState,
            [nameof(OptionRow.IsAvailable)] = MapState,
            [nameof(OptionRow.Title)] = MapState,
            [nameof(OptionRow.Helper)] = MapState,
            [nameof(CheckboxRow.IsChecked)] = MapState,
            [nameof(CheckboxRow.Text)] = MapState,
            [nameof(CheckboxRow.ErrorText)] = MapState,
            [nameof(CheckboxRow.HasError)] = MapState,
            [nameof(CheckboxRow.IsInputBlocked)] = MapState,
        };

    public KeyboardRowHandler()
        : base(RowMapper, ViewCommandMapper)
    {
    }

    protected override KeyboardRowHost CreatePlatformView() =>
        new(VirtualView ?? throw new InvalidOperationException("VirtualView must be set to create a keyboard row."));

    protected override void ConnectHandler(KeyboardRowHost platformView)
    {
        base.ConnectHandler(platformView);
        platformView.View = VirtualView;
        platformView.Refresh();
    }

    protected override void DisconnectHandler(KeyboardRowHost platformView)
    {
        platformView.View = null;
        base.DisconnectHandler(platformView);
    }

    private static void MapContent(KeyboardRowHandler handler, IContentView view)
    {
        if (handler.MauiContext is not { } context)
        {
            return;
        }

        var content = view.PresentedContent?.ToPlatform(context);
        if (content is not null && VisualTreeHelper.GetParent(content) is Panel parent && !ReferenceEquals(parent, handler.PlatformView))
        {
            parent.Children.Remove(content);
        }

        handler.PlatformView.Content = content;
    }

    private static void MapEnabled(KeyboardRowHandler handler, IContentView view)
    {
        ViewHandler.MapIsEnabled(handler, view);
        handler.PlatformView.Refresh();
    }

    private static void MapState(KeyboardRowHandler handler, IContentView view) => handler.PlatformView.Refresh();
}

public sealed partial class KeyboardRowHost : UserControl
{
    public KeyboardRowHost(IContentView view)
    {
        View = view;
        IsTabStop = true;
        UseSystemFocusVisuals = true;
        Background = new Microsoft.UI.Xaml.Media.SolidColorBrush(Microsoft.UI.Colors.Transparent);
        HorizontalContentAlignment = Microsoft.UI.Xaml.HorizontalAlignment.Stretch;
        VerticalContentAlignment = Microsoft.UI.Xaml.VerticalAlignment.Stretch;
        SizeChanged += (_, _) => Refresh();
    }

    public IContentView? View { get; set; }

    public IKeyboardRow? Row => View as IKeyboardRow;

    public void Refresh()
    {
        if (Row is not { } row)
        {
            return;
        }

        IsTabStop = row.IsOperable;
        FocusVisuals.Round(this, row.FocusCornerRadius);
        FocusVisuals.Apply(this, FocusRing.LightColorKey, FocusVisuals.MarginAround(row.FocusInset));
        (FrameworkElementAutomationPeer.FromElement(this) as KeyboardRowAutomationPeer)?.Update();
    }

    protected override PlatformSize MeasureOverride(PlatformSize availableSize)
    {
        if (View is not { } view)
        {
            return base.MeasureOverride(availableSize);
        }

        var size = view.CrossPlatformMeasure(availableSize.Width, availableSize.Height);
        return new PlatformSize(size.Width, size.Height);
    }

    protected override PlatformSize ArrangeOverride(PlatformSize finalSize)
    {
        if (View is not { } view)
        {
            return base.ArrangeOverride(finalSize);
        }

        view.CrossPlatformArrange(new Rect(0, 0, finalSize.Width, finalSize.Height));
        return finalSize;
    }

    protected override void OnKeyDown(KeyRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (Row is { } row && KeyboardRows.Press(row, KeyOf(e.Key), released: false))
        {
            e.Handled = true;
            return;
        }

        base.OnKeyDown(e);
    }

    protected override void OnKeyUp(KeyRoutedEventArgs e)
    {
        ArgumentNullException.ThrowIfNull(e);

        if (Row is { } row && KeyboardRows.Press(row, KeyOf(e.Key), released: true))
        {
            e.Handled = true;
            return;
        }

        base.OnKeyUp(e);
    }

    protected override AutomationPeer OnCreateAutomationPeer() => new KeyboardRowAutomationPeer(this);

    private static KeyboardRowKey KeyOf(VirtualKey key) => key switch
    {
        VirtualKey.Space => KeyboardRowKey.Space,
        VirtualKey.Enter => KeyboardRowKey.Enter,
        _ => KeyboardRowKey.Other,
    };
}

public sealed partial class KeyboardRowAutomationPeer : FrameworkElementAutomationPeer, ISelectionItemProvider, IToggleProvider
{
    private readonly KeyboardRowHost _host;
    private string? _name;
    private bool? _isOn;

    public KeyboardRowAutomationPeer(KeyboardRowHost owner)
        : base(owner)
    {
        _host = owner;
    }

    public bool IsSelected => _host.Row?.IsOn == true;

    public IRawElementProviderSimple SelectionContainer
    {
        get
        {
            for (var node = VisualTreeHelper.GetParent(_host); node is not null; node = VisualTreeHelper.GetParent(node))
            {
                if (node is OptionGroupPanel group && CreatePeerForElement(group) is { } peer)
                {
                    return ProviderFromPeer(peer);
                }
            }

            return null!;
        }
    }

    public ToggleState ToggleState => _host.Row?.IsOn == true ? ToggleState.On : ToggleState.Off;

    public void AddToSelection() => Select();

    public void RemoveFromSelection()
    {
    }

    public void Select()
    {
        if (_host.Row is { IsOperable: true } row && !row.IsOn)
        {
            row.Operate();
        }
    }

    public void Toggle()
    {
        if (_host.Row is { IsOperable: true } row)
        {
            row.Operate();
        }
    }

    public void Update()
    {
        if (_host.Row is not { } row)
        {
            return;
        }

        var name = row.AccessibleName;
        if (_name is not null && name != _name)
        {
            RaisePropertyChangedEvent(AutomationElementIdentifiers.NameProperty, _name, name);
        }

        if (_isOn is { } wasOn && wasOn != row.IsOn)
        {
            if (row.Role == KeyboardRowRole.RadioButton)
            {
                RaisePropertyChangedEvent(SelectionItemPatternIdentifiers.IsSelectedProperty, wasOn, row.IsOn);
                if (row.IsOn)
                {
                    RaiseAutomationEvent(AutomationEvents.SelectionItemPatternOnElementSelected);
                }
            }
            else
            {
                RaisePropertyChangedEvent(TogglePatternIdentifiers.ToggleStateProperty, wasOn ? ToggleState.On : ToggleState.Off, row.IsOn ? ToggleState.On : ToggleState.Off);
            }
        }

        _name = name;
        _isOn = row.IsOn;
    }

    protected override object GetPatternCore(PatternInterface patternInterface) => (patternInterface, _host.Row?.Role) switch
    {
        (PatternInterface.SelectionItem, KeyboardRowRole.RadioButton) => this,
        (PatternInterface.Toggle, KeyboardRowRole.CheckBox) => this,
        _ => base.GetPatternCore(patternInterface),
    };

    protected override AutomationControlType GetAutomationControlTypeCore() =>
        _host.Row?.Role == KeyboardRowRole.CheckBox ? AutomationControlType.CheckBox : AutomationControlType.RadioButton;

    protected override string GetClassNameCore() => _host.View?.GetType().Name ?? nameof(KeyboardRowHost);

    protected override string GetNameCore() => _host.Row?.AccessibleName ?? string.Empty;

    protected override string GetFullDescriptionCore() => _host.Row?.AccessibleDetail ?? string.Empty;

    protected override bool IsEnabledCore() => _host.Row?.IsOperable == true;

    protected override bool IsKeyboardFocusableCore() => _host.Row?.IsOperable == true;

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;

    protected override IList<AutomationPeer> GetChildrenCore() => [];
}
