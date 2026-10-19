namespace RecipeBox.Mobile;

// Carries an extracted photo/URL draft from the capture and import pages to the recipe form.
public sealed class RecipeDraftState
{
    public RecipeDraftResponse? Pending { get; set; }

    public RecipeDraftResponse? Take()
    {
        var draft = Pending;
        Pending = null;
        return draft;
    }
}
