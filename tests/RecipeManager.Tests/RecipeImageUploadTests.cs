using RecipeManager.Web.Services;

namespace RecipeManager.Tests;

public sealed class RecipeImageUploadTests
{
    [Theory]
    [InlineData(new byte[] { 0xFF, 0xD8, 0xFF, 0xD9 })]
    [InlineData(new byte[] { 0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A })]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x57, 0x45, 0x42, 0x50 })]
    public void HasSupportedSignature_AcceptsJpegPngAndWebp(byte[] imageHeader)
    {
        Assert.True(RecipeImageUpload.HasSupportedSignature(imageHeader));
    }

    [Theory]
    [InlineData(new byte[] { 0x00, 0x01, 0x02 })]
    [InlineData(new byte[] { 0x52, 0x49, 0x46, 0x46, 0, 0, 0, 0, 0x4A, 0x50, 0x45, 0x47 })]
    public void HasSupportedSignature_RejectsUnsupportedOrInvalidImages(byte[] imageHeader)
    {
        Assert.False(RecipeImageUpload.HasSupportedSignature(imageHeader));
    }

    [Fact]
    public void IsWithinSizeLimit_RejectsEmptyAndOversizedUploads()
    {
        Assert.False(RecipeImageUpload.IsWithinSizeLimit(0));
        Assert.True(RecipeImageUpload.IsWithinSizeLimit(RecipeImageUpload.MaximumBytes));
        Assert.False(RecipeImageUpload.IsWithinSizeLimit(RecipeImageUpload.MaximumBytes + 1));
    }
}
