namespace RecipeBox.Mobile;

public partial class App : Application
{
	private readonly MobileApiClient mobileApiClient;

	public App(MobileApiClient mobileApiClient)
	{
		InitializeComponent();
		this.mobileApiClient = mobileApiClient;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage(mobileApiClient));
	}
}