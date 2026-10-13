using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using RecipeBox.Web.Models;

namespace RecipeBox.Web.Data;

public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext(options)
{
    public DbSet<Recipe> Recipes => Set<Recipe>();

    public DbSet<RecipeLayoutPreference> RecipeLayouts => Set<RecipeLayoutPreference>();

    public DbSet<RecipeShare> RecipeShares => Set<RecipeShare>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);

        builder.Entity<Recipe>()
            .HasOne<Microsoft.AspNetCore.Identity.IdentityUser>()
            .WithMany()
            .HasForeignKey(recipe => recipe.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<Recipe>()
            .HasIndex(recipe => recipe.StoredImageKey)
            .IsUnique()
            .HasFilter("\"StoredImageKey\" IS NOT NULL");

        builder.Entity<RecipeLayoutPreference>()
            .HasKey(layout => new { layout.RecipeId, layout.OwnerId });

        builder.Entity<RecipeLayoutPreference>()
            .HasOne<Recipe>()
            .WithMany()
            .HasForeignKey(layout => layout.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<RecipeLayoutPreference>()
            .HasOne<Microsoft.AspNetCore.Identity.IdentityUser>()
            .WithMany()
            .HasForeignKey(layout => layout.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<RecipeLayoutPreference>()
            .HasIndex(layout => layout.OwnerId);

        builder.Entity<RecipeShare>()
            .HasKey(share => new { share.RecipeId, share.OwnerId });

        builder.Entity<RecipeShare>()
            .HasOne<Recipe>()
            .WithOne()
            .HasForeignKey<RecipeShare>(share => share.RecipeId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<RecipeShare>()
            .HasOne<Microsoft.AspNetCore.Identity.IdentityUser>()
            .WithMany()
            .HasForeignKey(share => share.OwnerId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.Entity<RecipeShare>()
            .HasIndex(share => share.TokenHash)
            .IsUnique();

        builder.Entity<RecipeShare>()
            .HasIndex(share => share.OwnerId);
    }
}
