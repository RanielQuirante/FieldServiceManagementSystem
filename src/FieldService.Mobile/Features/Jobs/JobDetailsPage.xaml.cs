namespace FieldService.Mobile.Features.Jobs;

public partial class JobDetailsPage : ContentPage
{
    private readonly JobDetailsViewModel _viewModel;

    public JobDetailsPage(JobDetailsViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
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
