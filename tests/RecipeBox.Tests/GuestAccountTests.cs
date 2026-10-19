using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RecipeBox.Web.Data;
using RecipeBox.Web.Models;
using RecipeBox.Web.Services;

namespace RecipeBox.Tests;

public sealed class GuestAccountTests
{
    [Theory]
    [InlineData("guest-0123abcd", true)]
    [InlineData("guest-someone@example.com", false)]
    [InlineData("alice@example.com", false)]
    [InlineData(null, false)]
    public void IsGuestUserName_OnlyMatchesGeneratedGuests(string? name, bool expected) =>
        Assert.Equal(expected, GuestAccountService.IsGuestUserName(name));

    [Fact]
    public async Task Merge_MovesRecipesToAccountAndDeletesGuest()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
        var users = scope.ServiceProvider.GetRequiredService<UserManager<IdentityUser>>();
        var service = scope.ServiceProvider.GetRequiredService<GuestAccountService>();

        var guest = new IdentityUser { UserName = GuestAccountService.UserNamePrefix + "abc123" };
        Assert.True((await users.CreateAsync(guest)).Succeeded);
        db.Recipes.Add(new Recipe
        {
            Title = "Guest soup",
            Ingredients = "water",
            Instructions = "boil",
            OwnerId = guest.Id,
        });
        await db.SaveChangesAsync();

        var moved = await service.MergeAsync(guest.Id, RecipeAppFactory.TestUserId, CancellationToken.None);

        Assert.Equal(1, moved);
        Assert.Null(await users.FindByIdAsync(guest.Id));
        Assert.True(await db.Recipes.AnyAsync(r =>
            r.Title == "Guest soup" && r.OwnerId == RecipeAppFactory.TestUserId));
    }

    [Fact]
    public async Task Merge_IgnoresNonGuestUsers()
    {
        using var factory = new RecipeAppFactory();
        using var client = factory.CreateAppClient();
        using var scope = factory.Services.CreateScope();
        var service = scope.ServiceProvider.GetRequiredService<GuestAccountService>();

        var moved = await service.MergeAsync(RecipeAppFactory.TestUserId, "someone-else", CancellationToken.None);

        Assert.Equal(0, moved);
    }
}
