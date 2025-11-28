namespace StrongBuy.Blazor.Models;

/// <summary>
/// 用戶操作事件類型
/// </summary>
public enum UserActionType
{
    /// <summary>
    /// 瀏覽商品（查看商品詳情頁）
    /// </summary>
    ViewProduct = 1,

    /// <summary>
    /// 查詢關鍵字（執行搜尋）
    /// </summary>
    SearchQuery = 2,

    /// <summary>
    /// 點擊商品（在搜尋結果中點擊商品）
    /// </summary>
    ClickProduct = 3,

    /// <summary>
    /// 加入購物車
    /// </summary>
    AddToCart = 4,

    /// <summary>
    /// 結帳（購買商品）
    /// </summary>
    Checkout = 5
}

/// <summary>
/// 用戶操作事件模型
/// 記錄用戶的所有互動行為，用於個人化推薦
/// </summary>
public class UserAction
{
    /// <summary>
    /// 操作 ID（唯一識別碼）
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 用戶 ID（或 Session ID，用於識別用戶）
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// Persona ID（關聯到 Persona）
    /// </summary>
    public string? PersonaId { get; set; }

    /// <summary>
    /// 操作類型
    /// </summary>
    public UserActionType ActionType { get; set; }

    /// <summary>
    /// 商品 ID（如果是商品相關操作）
    /// </summary>
    public int? ProductId { get; set; }

    /// <summary>
    /// 商品名稱（快照，避免商品被刪除後無法追溯）
    /// </summary>
    public string? ProductName { get; set; }

    /// <summary>
    /// 商品分類（快照）
    /// </summary>
    public string? ProductCategory { get; set; }

    /// <summary>
    /// 商品品牌（快照）
    /// </summary>
    public string? ProductBrand { get; set; }

    /// <summary>
    /// 搜尋關鍵字（如果是搜尋操作）
    /// </summary>
    public string? SearchQuery { get; set; }

    /// <summary>
    /// 搜尋結果數量（如果是搜尋操作）
    /// </summary>
    public int? SearchResultCount { get; set; }

    /// <summary>
    /// 操作時間戳記
    /// </summary>
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 額外的上下文資訊（JSON 格式，用於儲存其他相關資訊）
    /// 例如：搜尋時使用的 filter、排序選項等
    /// </summary>
    public string? AdditionalContext { get; set; }
}

