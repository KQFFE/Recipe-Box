using RecipeBox.Mobile.Core;

namespace RecipeBox.Mobile;

public partial class App : Application
{
	private readonly MobileApiClient mobileApiClient;
	private readonly LocalRecipeStore recipeStore;
	private readonly ProfileSyncService profileSync;

	public App(
		MobileApiClient mobileApiClient,
		LocalRecipeStore recipeStore,
		ProfileSyncService profileSync)
	{
		InitializeComponent();
		this.mobileApiClient = mobileApiClient;
		this.recipeStore = recipeStore;
		this.profileSync = profileSync;
	}

	protected override Window CreateWindow(IActivationState? activationState)
	{
		return new Window(new MainPage(mobileApiClient, recipeStore, profileSync));
	}
}