namespace RecipeManager.Web.Services;

public interface IRecipeTextExtractor
{
    string ExtractText(byte[] imageBytes);
}
