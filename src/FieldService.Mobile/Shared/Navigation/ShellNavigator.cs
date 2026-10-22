namespace FieldService.Mobile.Shared.Navigation;

public sealed class ShellNavigator : INavigator
{
    public Task GoToAsync(string route) =>
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current is { } shell ? shell.GoToAsync(route) : Task.CompletedTask);

    public Task GoToAsync(string route, IDictionary<string, object> parameters) =>
        MainThread.InvokeOnMainThreadAsync(() => Shell.Current is { } shell
            ? shell.GoToAsync(route, new ShellNavigationQueryParameters(parameters))
            : Task.CompletedTask);
}
