using TesseractOCR;
using TesseractOCR.Enums;
using TesseractOCR.Pix;

namespace RecipeBox.Web.Services;

public sealed class TesseractRecipeTextExtractor(string tessdataPath) : IRecipeTextExtractor
{
    public string ExtractText(byte[] imageBytes)
    {
        ArgumentNullException.ThrowIfNull(imageBytes);

        if (imageBytes.Length == 0)
        {
            throw new ArgumentException("The image must not be empty.", nameof(imageBytes));
        }

        var missingModels = new[] { "eng.traineddata", "swe.traineddata" }
            .Where(model => !File.Exists(Path.Combine(tessdataPath, model)))
            .ToArray();
        if (missingModels.Length > 0)
        {
            throw new InvalidOperationException(
                $"Required OCR model(s) were not found in '{tessdataPath}': {string.Join(", ", missingModels)}.");
        }

        using var engine = new Engine(tessdataPath, "eng+swe", EngineMode.Default);
        using var image = Image.LoadFromMemory(imageBytes);
        using var page = engine.Process(image);

        return page.Text;
    }
}
