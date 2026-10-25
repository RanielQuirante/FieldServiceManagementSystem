using FieldService.Mobile.Shared.Controls;
using Microsoft.Maui.Handlers;
using Microsoft.Maui.Platform;
using Microsoft.UI.Xaml.Automation.Peers;
using Microsoft.UI.Xaml.Automation.Provider;

namespace FieldService.Mobile.WinUI;

public sealed partial class OptionGroupHandler : LayoutHandler
{
    protected override LayoutPanel CreatePlatformView() =>
        new OptionGroupPanel { CrossPlatformLayout = VirtualView ?? throw new InvalidOperationException("VirtualView must be set to create an option group.") };
}

public sealed partial class OptionGroupPanel : LayoutPanel
{
    protected override AutomationPeer OnCreateAutomationPeer() => new OptionGroupAutomationPeer(this);
}

public sealed partial class OptionGroupAutomationPeer : FrameworkElementAutomationPeer, ISelectionProvider
{
    private readonly OptionGroupPanel _panel;

    public OptionGroupAutomationPeer(OptionGroupPanel owner)
        : base(owner)
    {
        _panel = owner;
    }

    public bool CanSelectMultiple => false;

    public bool IsSelectionRequired => false;

    public IRawElementProviderSimple[] GetSelection() =>
        [.. _panel.Children
            .OfType<KeyboardRowHost>()
            .Where(host => host.Row?.IsOn == true)
            .Select(CreatePeerForElement)
            .Where(peer => peer is not null)
            .Select(ProviderFromPeer)];

    protected override object GetPatternCore(PatternInterface patternInterface) =>
        patternInterface == PatternInterface.Selection ? this : base.GetPatternCore(patternInterface);

    protected override AutomationControlType GetAutomationControlTypeCore() => AutomationControlType.Group;

    protected override string GetClassNameCore() => nameof(OptionGroup);

    protected override string GetNameCore() => (_panel.CrossPlatformLayout as OptionGroup)?.Title ?? string.Empty;

    protected override bool IsControlElementCore() => true;

    protected override bool IsContentElementCore() => true;
}
