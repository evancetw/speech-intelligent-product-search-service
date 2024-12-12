using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using StrongBuy.Blazor.Models;

public class StrongBuyContext : DbContext
{
    public StrongBuyContext(DbContextOptions<StrongBuyContext> options)
        : base(options)
    {
    }

    public DbSet<Product> Products { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // modelBuilder.Entity<Product>()
        //     .Property(p => p.Price)
        //     .HasConversion<double>();

        modelBuilder.Entity<Product>()
            .Property(p => p.Subcategories)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList());

        modelBuilder.Entity<Product>()
            .Property(p => p.Images)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList());

        modelBuilder.Entity<Product>()
            .Property(p => p.Tags)
            .HasConversion(
                v => string.Join(',', v),
                v => v.Split(',', StringSplitOptions.RemoveEmptyEntries).ToList());

        // JSON conversion for complex types
        modelBuilder.Entity<Product>()
            .Property(p => p.Attributes)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v => System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(v,
                    JsonSerializerOptions.Default) ?? new());

        modelBuilder.Entity<Product>()
            .Property(p => p.Reviews)
            .HasConversion(
                v => System.Text.Json.JsonSerializer.Serialize(v, JsonSerializerOptions.Default),
                v =>
                    System.Text.Json.JsonSerializer.Deserialize<List<ProductReview>>(v,
                        JsonSerializerOptions.Default) ?? new());
    }
}