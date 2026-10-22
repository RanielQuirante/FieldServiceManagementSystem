namespace FieldService.Mobile.Shared.Navigation;

public interface INavigator
{
    Task GoToAsync(string route);

    Task GoToAsync(string route, IDictionary<string, object> parameters);
}
