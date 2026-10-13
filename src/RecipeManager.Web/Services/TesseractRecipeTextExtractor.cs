using TesseractOCR;
using TesseractOCR.Enums;
using TesseractOCR.Pix;

namespace RecipeManager.Web.Services;

public sealed class TesseractRecipeTextExtractor(string tessdataPath) : IRecipeTextExtractor
{
    public string ExtractText(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        if (imageBytes.Length == 0)
        {
            throw new ArgumentException("The image must not be empty.", nameof(imageBytes));
        }

        var englishDataPath = Path.Combine(tessdataPath, "eng.traineddata");
        if (!File.Exists(englishDataPath))
        {
            throw new InvalidOperationException(
                $"The English OCR model was not found at '{englishDataPath}'.");
        }

        using var engine = new Engine(tessdataPath, Language.English, EngineMode.Default);
        using var image = Image.LoadFromMemory(imageBytes);
        using var page = engine.Process(image);

        return page.Text;
    }
}
