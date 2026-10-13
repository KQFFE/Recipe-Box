namespace RecipeBox.Web.Services;

public interface IRecipeTextExtractor
{
    string ExtractText(byte[] imageBytes);
}
