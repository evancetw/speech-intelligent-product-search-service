using System.Text.Json.Serialization;

namespace StrongBuy.Blazor.Models;

/// <summary>
/// Product Inventory 模型
/// 對應 product_inventory.json 的結構
/// </summary>
public class ProductInventory
{
    [JsonPropertyName("categories")]
    public List<CategoryInfo> Categories { get; set; } = new();

    [JsonPropertyName("brands_by_category")]
    public Dictionary<string, List<string>> BrandsByCategory { get; set; } = new();
}

public class CategoryInfo
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("product_count")]
    public int ProductCount { get; set; }

    [JsonPropertyName("brand_count")]
    public int BrandCount { get; set; }
}

