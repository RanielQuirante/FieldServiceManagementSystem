namespace FieldService.Mobile.Shared.Screens;

public interface IWindowLayout
{
    bool IsTwoPane { get; }

    event EventHandler? Changed;
}

public sealed class WindowLayout(IDeviceInfo deviceInfo) : IWindowLayout
{
    public const double TwoPaneMinimumWidth = 900;

    private Window? _window;

    public event EventHandler? Changed;

    public bool IsTwoPane { get; private set; }

    public static bool IsTwoPaneFor(DeviceIdiom idiom, double width) =>
        idiom == DeviceIdiom.Desktop && width >= TwoPaneMinimumWidth;

    public void Attach(Window window)
    {
        ArgumentNullException.ThrowIfNull(window);

        if (_window is not null)
        {
            _window.SizeChanged -= OnSizeChanged;
        }

        _window = window;
        _window.SizeChanged += OnSizeChanged;
        Update();
    }

    private void OnSizeChanged(object? sender, EventArgs e) => Update();

    private void Update()
    {
        var next = _window is not null && IsTwoPaneFor(deviceInfo.Idiom, _window.Width);
        if (next == IsTwoPane)
        {
            return;
        }

        IsTwoPane = next;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
