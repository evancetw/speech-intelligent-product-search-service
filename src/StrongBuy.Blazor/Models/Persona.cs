namespace StrongBuy.Blazor.Models;

/// <summary>
/// 用戶 Persona 模型
/// 用於定義不同用戶角色的特徵和偏好
/// </summary>
public class Persona
{
    /// <summary>
    /// Persona ID（唯一識別碼）
    /// </summary>
    public string Id { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// 職業（例如：廚師、通勤上班族、戶外運動族、敏感肌族群）
    /// </summary>
    public string Occupation { get; set; } = string.Empty;

    /// <summary>
    /// 個人描述（詳細描述用戶的背景、興趣、需求）
    /// 例如：
    /// - "我是一位廚師，我喜歡美食，最近看了黑白大廚，很喜歡"
    /// - "我是我家的首富，服務都要最好的"
    /// - "通勤上班族，想要「不卡粉、可當妝前乳」的防曬"
    /// - "戶外運動族，重視「防汗、防水、長效」"
    /// - "敏感肌族群，關鍵字偏向「物理性、防敏、無酒精」"
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// 喜好的分類列表（用於 scrolling profile）
    /// 例如：["美食", "廚具", "調味料"]
    /// </summary>
    public List<string> PreferredCategories { get; set; } = new();

    /// <summary>
    /// 關鍵字偏好（用於搜尋優化）
    /// 例如：["物理性", "防敏", "無酒精"]
    /// </summary>
    public List<string> PreferredKeywords { get; set; } = new();

    /// <summary>
    /// 創建時間
    /// </summary>
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// 更新時間
    /// </summary>
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

