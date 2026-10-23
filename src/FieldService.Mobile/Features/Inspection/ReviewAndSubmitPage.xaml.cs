namespace FieldService.Mobile.Features.Inspection;

public partial class ReviewAndSubmitPage : ContentPage
{
    private readonly ReviewAndSubmitViewModel _viewModel;

    public ReviewAndSubmitPage(ReviewAndSubmitViewModel viewModel)
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

    protected override bool OnBackButtonPressed() => _viewModel.HandleBackButton();
}
