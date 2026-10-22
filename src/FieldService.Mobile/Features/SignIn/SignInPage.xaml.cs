namespace FieldService.Mobile.Features.SignIn;

public partial class SignInPage : ContentPage
{
    private readonly SignInViewModel _viewModel;

    public SignInPage(SignInViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        Loaded += (_, _) => _viewModel.LoadedCommand.Execute(null);
    }

    protected override void OnAppearing()
    {
        base.OnAppearing();
        _viewModel.AppearingCommand.Execute(null);
    }

    protected override void OnNavigatedTo(NavigatedToEventArgs args)
    {
        base.OnNavigatedTo(args);
        _viewModel.NavigatedToCommand.Execute(null);
    }
}
