namespace FieldService.Mobile.Features.Jobs;

public partial class MyJobsPage : ContentPage
{
    private readonly MyJobsViewModel _viewModel;

    public MyJobsPage(MyJobsViewModel viewModel)
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

    protected override void OnDisappearing()
    {
        _viewModel.DisappearingCommand.Execute(null);
        base.OnDisappearing();
    }

    protected override bool OnBackButtonPressed() => _viewModel.CloseSheet() || base.OnBackButtonPressed();
}
