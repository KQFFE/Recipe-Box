using Microsoft.Extensions.Logging;

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

#if DEBUG
		builder.Logging.AddDebug();
#endif

		return builder.Build();
	}
}
