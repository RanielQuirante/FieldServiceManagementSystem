namespace FieldService.Mobile.Features.Inspection;

public partial class InspectionPage : ContentPage
{
    private readonly InspectionViewModel _viewModel;

    public InspectionPage(InspectionViewModel viewModel)
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
