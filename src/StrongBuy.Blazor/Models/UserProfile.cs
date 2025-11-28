namespace StrongBuy.Blazor.Models;

/// <summary>
/// 用戶個人化檔案模型
/// 整合 Persona 和用戶行為，用於生成個人化推薦
/// </summary>
public class UserProfile
{
    /// <summary>
    /// 用戶 ID（或 Session ID）
    /// </summary>
    public string UserId { get; set; } = string.Empty;

    /// <summary>
    /// 當前使用的 Persona
    /// </summary>
    public Persona? Persona { get; set; }

    /// <summary>
    /// 用戶的操作歷史（最近 N 筆）
    /// </summary>
    public List<UserAction> RecentActions { get; set; } = new();

    /// <summary>
    /// 動態偏好向量（基於用戶行為計算得出）
    /// 用於向量搜尋的個人化調整
    /// </summary>
    public float[]? PreferenceVector { get; set; }

    /// <summary>
    /// 偏好分類（從行為中提取）
    /// </summary>
    public Dictionary<string, int> PreferredCategories { get; set; } = new();

    /// <summary>
    /// 偏好品牌（從行為中提取）
    /// </summary>
    public Dictionary<string, int> PreferredBrands { get; set; } = new();

    /// <summary>
    /// 偏好關鍵字（從搜尋和點擊行為中提取）
    /// </summary>
    public Dictionary<string, int> PreferredKeywords { get; set; } = new();

    /// <summary>
    /// 最後更新時間
    /// </summary>
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
}

