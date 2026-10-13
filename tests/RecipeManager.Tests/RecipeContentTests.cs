using RecipeManager.Web.Models;

namespace RecipeManager.Tests;

public sealed class RecipeContentTests
{
    [Fact]
    public void NormalizeLines_TrimsEntriesAndRemovesBlankLines()
    {
        var content = "  2 eggs \r\n\r\n 1 cup flour  \n";

        var result = RecipeContent.NormalizeLines(content);

        Assert.Equal($"2 eggs{Environment.NewLine}1 cup flour", result);
    }

    [Fact]
    public void ToLines_ReturnsSeparateOrderedItems()
    {
        var result = RecipeContent.ToLines("Mix well\r\nBake for 20 minutes");

        Assert.Equal(new[] { "Mix well", "Bake for 20 minutes" }, result);
    }

    [Fact]
    public void ToLines_ReturnsEmptyListForBlankContent()
    {
        Assert.Empty(RecipeContent.ToLines(" \r\n "));
    }
}
