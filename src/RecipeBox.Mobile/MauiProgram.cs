using Microsoft.Extensions.Logging;
using RecipeBox.Mobile.Core;

namespace RecipeBox.Mobile;

public static class MauiProgram
{
	public static MauiApp CreateMauiApp()
	{
		var builder = MauiApp.CreateBuilder();
		builder
			.UseMauiApp<App>()
			.ConfigureFonts(fonts =>
			{
				fonts.AddFont("OpenSans-Regular.ttf", "OpenSansRegular");
				fonts.AddFont("OpenSans-Semibold.ttf", "OpenSansSemibold");
			});
		builder.Services.AddSingleton(new HttpClient(new HttpClientHandler
		{
			AllowAutoRedirect = false
		}));
		builder.Services.AddSingleton<MobileTokenStore>();
		builder.Services.AddSingleton<MobileApiClient>();
		builder.Services.AddSingleton<IRecipeProfileApi>(services =>
			services.GetRequiredService<MobileApiClient>());
		builder.Services.AddSingleton(_ => new LocalRecipeStore(
			Path.Combine(FileSystem.AppDataDirectory, "recipes.db"),
			Path.Combine(FileSystem.AppDataDirectory, "recipe-images")));
		builder.Services.AddSingleton<ProfileSyncService>();

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
