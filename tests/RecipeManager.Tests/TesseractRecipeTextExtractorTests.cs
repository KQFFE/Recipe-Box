using RecipeManager.Web.Services;

namespace RecipeManager.Tests;

public sealed class TesseractRecipeTextExtractorTests
{
    [Fact]
    public void ExtractText_RecognizesTextFromPinnedTesseractFixture()
    {
        var tessdataPath = Path.Combine(AppContext.BaseDirectory, "tessdata");
        var imagePath = Path.Combine(AppContext.BaseDirectory, "TestData", "phototest.tif");
        var extractor = new TesseractRecipeTextExtractor(tessdataPath);

        var result = extractor.ExtractText(File.ReadAllBytes(imagePath));

        Assert.Contains("This is a lot of 12 point text to test the", result);
        Assert.Contains("The quick brown dog jumped over the", result);
    }
}
