namespace StrongBuy.Blazor.Models;

public class ProductV2
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;
    public ReadOnlyMemory<float>? NameEmbedding { get; set; }

    public string Description { get; set; } = string.Empty;
    public ReadOnlyMemory<float>? DescriptionEmbedding { get; set; }

    // public decimal Price { get; set; }
    public double Price { get; set; }
    public string Category { get; set; } = string.Empty;
    public List<string> Subcategories { get; set; } = new();
    public string Brand { get; set; } = string.Empty;
    public string Color { get; set; } = string.Empty;
    public string Size { get; set; } = string.Empty;
    public string Material { get; set; } = string.Empty;
    public string Image { get; set; } = string.Empty;
    public List<string> Images { get; set; } = new();
    public List<string> Tags { get; set; } = new();
    public Dictionary<string, string> Attributes { get; set; } = new();

    public List<ProductReview> Reviews { get; set; } = new();
    public ReadOnlyMemory<float>? ReviewsEmbedding { get; set; }

    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    
    
    public ReadOnlyMemory<float>? CombinedEmbedding { get; set; }
}