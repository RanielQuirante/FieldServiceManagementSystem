using FieldService.Mobile.Shared.Screens;

namespace FieldService.Mobile.Shared.Navigation;

public sealed class ShellNavigator(IWindowLayout layout) : INavigator
{
    public Task GoToAsync(string route) =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (Shell.Current is not { } shell)
            {
                return Task.CompletedTask;
            }

            var handoff = PassOver(shell.Navigation, route);
            return handoff.Count == 0 ? shell.GoToAsync(route) : shell.GoToAsync(route, new ShellNavigationQueryParameters(handoff));
        });

    public Task GoToAsync(string route, IDictionary<string, object> parameters) => GoToAsync(route, parameters, animate: true);

    public Task GoToAsync(string route, IDictionary<string, object> parameters, bool animate) =>
        MainThread.InvokeOnMainThreadAsync(() =>
        {
            if (Shell.Current is not { } shell)
            {
                return Task.CompletedTask;
            }

            var query = new Dictionary<string, object>(parameters);
            foreach (var (key, value) in PassOver(shell.Navigation, route))
            {
                query.TryAdd(key, value);
            }

            return shell.GoToAsync(route, animate, new ShellNavigationQueryParameters(query));
        });

    public static Page? PhoneOnlyPageBelowTop(IReadOnlyList<Page?> stack, bool twoPane, string route)
    {
        ArgumentNullException.ThrowIfNull(stack);

        return twoPane && route == Routes.Back && stack.Count >= 3 && stack[^2] is { BindingContext: IPaneHandoffSource { PaneHandoff: not null } } below
            ? below
            : null;
    }

    private Dictionary<string, object> PassOver(INavigation navigation, string route)
    {
        if (PhoneOnlyPageBelowTop(navigation.NavigationStack, layout.IsTwoPane, route) is not { BindingContext: IPaneHandoffSource { PaneHandoff: { } handoff } } below)
        {
            return [];
        }

        navigation.RemovePage(below);
        return new Dictionary<string, object> { [Routes.PaneJobParameter] = handoff };
    }
}
