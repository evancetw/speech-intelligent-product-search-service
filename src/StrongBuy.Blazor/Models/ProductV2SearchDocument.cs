using System.Text.Json;
using System.Text.Json.Serialization;
using StrongBuy.Core.Models;

namespace StrongBuy.Blazor.Models;

/// <summary>
/// ProductV2 的 Azure AI Search 文檔模型
/// 用於序列化上傳到 Azure AI Search
/// </summary>
public class ProductV2SearchDocument
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("nameEmbedding")]
    public float[]? NameEmbedding { get; set; }

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("descriptionEmbedding")]
    public float[]? DescriptionEmbedding { get; set; }

    [JsonPropertyName("price")]
    public double Price { get; set; }

    [JsonPropertyName("category")]
    public string Category { get; set; } = string.Empty;

    [JsonPropertyName("subcategories")]
    public List<string> Subcategories { get; set; } = new();

    [JsonPropertyName("brand")]
    public string Brand { get; set; } = string.Empty;

    [JsonPropertyName("color")]
    public string Color { get; set; } = string.Empty;

    [JsonPropertyName("size")]
    public string Size { get; set; } = string.Empty;

    [JsonPropertyName("material")]
    public string Material { get; set; } = string.Empty;

    [JsonPropertyName("image")]
    public string Image { get; set; } = string.Empty;

    [JsonPropertyName("images")]
    public List<string> Images { get; set; } = new();

    [JsonPropertyName("tags")]
    public List<string> Tags { get; set; } = new();

    [JsonPropertyName("attributes")]
    public string? Attributes { get; set; }

    [JsonPropertyName("reviews")]
    public List<ProductReview> Reviews { get; set; } = new();

    [JsonPropertyName("reviewsEmbedding")]
    public float[]? ReviewsEmbedding { get; set; }

    [JsonPropertyName("createdAt")]
    public DateTimeOffset CreatedAt { get; set; }

    [JsonPropertyName("updatedAt")]
    public DateTimeOffset UpdatedAt { get; set; }

    [JsonPropertyName("combinedEmbedding")]
    public float[]? CombinedEmbedding { get; set; }

    /// <summary>
    /// 從 ProductV2 轉換為 ProductV2SearchDocument
    /// </summary>
    public static ProductV2SearchDocument FromProductV2(ProductV2 product)
    {
        return new ProductV2SearchDocument
        {
            Id = product.Id.ToString(),
            Name = product.Name,
            NameEmbedding = product.NameEmbedding?.ToArray(),
            Description = product.Description,
            DescriptionEmbedding = product.DescriptionEmbedding?.ToArray(),
            Price = product.Price,
            Category = product.Category,
            Subcategories = product.Subcategories,
            Brand = product.Brand,
            Color = product.Color,
            Size = product.Size,
            Material = product.Material,
            Image = product.Image,
            Images = product.Images,
            Tags = product.Tags,
            Attributes = product.Attributes != null && product.Attributes.Any() 
                ? JsonSerializer.Serialize(product.Attributes) 
                : null,
            Reviews = product.Reviews,
            ReviewsEmbedding = product.ReviewsEmbedding?.ToArray(),
            CreatedAt = product.CreatedAt == default ? DateTimeOffset.UtcNow : new DateTimeOffset(product.CreatedAt, TimeSpan.Zero),
            UpdatedAt = product.UpdatedAt == default ? DateTimeOffset.UtcNow : new DateTimeOffset(product.UpdatedAt, TimeSpan.Zero),
            CombinedEmbedding = product.CombinedEmbedding?.ToArray()
        };
    }

    /// <summary>
    /// 轉換為 ProductV2
    /// </summary>
    public ProductV2 ToProductV2()
    {
        return new ProductV2
        {
            Id = int.Parse(Id),
            Name = Name,
            NameEmbedding = NameEmbedding != null ? new ReadOnlyMemory<float>(NameEmbedding) : null,
            Description = Description,
            DescriptionEmbedding = DescriptionEmbedding != null ? new ReadOnlyMemory<float>(DescriptionEmbedding) : null,
            Price = Price,
            Category = Category,
            Subcategories = Subcategories,
            Brand = Brand,
            Color = Color,
            Size = Size,
            Material = Material,
            Image = Image,
            Images = Images,
            Tags = Tags,
            Attributes = !string.IsNullOrEmpty(Attributes) 
                ? JsonSerializer.Deserialize<Dictionary<string, string>>(Attributes) ?? new Dictionary<string, string>()
                : new Dictionary<string, string>(),
            Reviews = Reviews,
            ReviewsEmbedding = ReviewsEmbedding != null ? new ReadOnlyMemory<float>(ReviewsEmbedding) : null,
            CreatedAt = CreatedAt == default ? DateTime.UtcNow : CreatedAt.DateTime,
            UpdatedAt = UpdatedAt == default ? DateTime.UtcNow : UpdatedAt.DateTime,
            CombinedEmbedding = CombinedEmbedding != null ? new ReadOnlyMemory<float>(CombinedEmbedding) : null
        };
    }
}

