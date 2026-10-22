using FieldService.Mobile.Shared.Screens;

namespace FieldService.Mobile;

public partial class App : Application
{
    public const string WindowTitle = "FieldService Tech";

    private readonly AppShell _shell;
    private readonly WindowLayout _layout;

    public App(AppShell shell, WindowLayout layout)
    {
        InitializeComponent();
        UserAppTheme = AppTheme.Light;
        _shell = shell;
        _layout = layout;
    }

    protected override Window CreateWindow(IActivationState? activationState)
    {
        var window = new Window(_shell) { Title = WindowTitle };
        _layout.Attach(window);
        return window;
    }
}
