namespace RecipeBox.Mobile;

public partial class AppShell : Shell
{
	public AppShell()
	{
		InitializeComponent();
		Routing.RegisterRoute(nameof(RecipeDetailsPage), typeof(RecipeDetailsPage));
		Routing.RegisterRoute(nameof(RecipeFormPage), typeof(RecipeFormPage));
	}
}
