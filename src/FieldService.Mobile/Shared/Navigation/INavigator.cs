namespace FieldService.Mobile.Shared.Navigation;

public interface INavigator
{
    Task GoToAsync(string route);

    Task GoToAsync(string route, IDictionary<string, object> parameters);

    Task GoToAsync(string route, IDictionary<string, object> parameters, bool animate);
}

public interface IPaneHandoffSource
{
    object? PaneHandoff { get; }
}
