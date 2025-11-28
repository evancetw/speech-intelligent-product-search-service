using StrongBuy.Blazor.Models;
using OpenAI.Embeddings;

namespace StrongBuy.Blazor.Services;

/// <summary>
/// Persona 服務
/// 管理用戶 Persona 和行為追蹤，用於個人化推薦
/// </summary>
public class PersonaService
{
    private readonly Dictionary<string, UserProfile> _userProfiles = new();
    private readonly Dictionary<string, Persona> _personas = new();
    private readonly EmbeddingClient _embeddingClient;
    private readonly ILogger<PersonaService> _logger;

    public PersonaService(EmbeddingClient embeddingClient, ILogger<PersonaService> logger)
    {
        _embeddingClient = embeddingClient;
        _logger = logger;
        InitializeDefaultPersonas();
    }

    /// <summary>
    /// 初始化預設 Personas
    /// </summary>
    private void InitializeDefaultPersonas()
    {
        // 廚師 Persona
        _personas["chef"] = new Persona
        {
            Id = "chef",
            Occupation = "廚師",
            Description = "我是一位廚師，我喜歡美食，最近看了黑白大廚，很喜歡",
            PreferredCategories = new List<string> { "美食", "廚具", "調味料", "食材" },
            PreferredKeywords = new List<string> { "專業", "高品質", "料理" }
        };

        // 鋼鐵人 Persona
        _personas["tycoon"] = new Persona
        {
            Id = "tycoon",
            Occupation = "鋼鐵人",
            Description = "我是鋼鐵人，服務都要最好的",
            PreferredCategories = new List<string> { "奢侈品", "高級服務", "精品" },
            PreferredKeywords = new List<string> { "頂級", "奢華", "尊貴", "高品質" }
        };

        // 通勤上班族 Persona
        _personas["commuter"] = new Persona
        {
            Id = "commuter",
            Occupation = "上班族",
            Description = "女性上班族，想要「不卡粉、可當妝前乳」的防曬",
            PreferredCategories = new List<string> { "美妝", "防曬", "保養品" },
            PreferredKeywords = new List<string> { "不卡粉", "妝前乳", "防曬", "輕薄" }
        };

        // 戶外運動族 Persona
        _personas["outdoor"] = new Persona
        {
            Id = "outdoor",
            Occupation = "水上運動員",
            Description = "戶外運動族，重視「防汗、防水、長效」",
            PreferredCategories = new List<string> { "運動用品", "防曬", "戶外裝備" },
            PreferredKeywords = new List<string> { "防汗", "防水", "長效", "戶外" }
        };

        // 敏感肌族群 Persona
        _personas["sensitive_skin"] = new Persona
        {
            Id = "sensitive_skin",
            Occupation = "演員",
            Description = "敏感肌族群，關鍵字偏向「物理性、防敏、無酒精」",
            PreferredCategories = new List<string> { "美妝", "保養品", "防曬" },
            PreferredKeywords = new List<string> { "物理性", "防敏", "無酒精", "溫和" }
        };

        // 耳機尋求者 Persona
        _personas["headphone_seeker"] = new Persona
        {
            Id = "headphone_seeker",
            Occupation = "工程師",
            Description = "耳機控，科技控",
            PreferredCategories = new List<string> { "電子產品", "耳機" },
            PreferredKeywords = new List<string> { "降噪", "通話清晰", "通勤", "會議", "ANC", "麥克風", "商務" }
        };
    }

    /// <summary>
    /// 取得所有可用的 Personas
    /// </summary>
    public List<Persona> GetAllPersonas()
    {
        return _personas.Values.ToList();
    }

    /// <summary>
    /// 根據 ID 取得 Persona
    /// </summary>
    public Persona? GetPersona(string personaId)
    {
        return _personas.TryGetValue(personaId, out var persona) ? persona : null;
    }

    /// <summary>
    /// 設定用戶的 Persona
    /// </summary>
    public void SetUserPersona(string userId, string personaId)
    {
        var persona = GetPersona(personaId);
        if (persona == null)
        {
            _logger.LogWarning("Persona {PersonaId} not found", personaId);
            return;
        }

        if (!_userProfiles.TryGetValue(userId, out var profile))
        {
            profile = new UserProfile { UserId = userId };
            _userProfiles[userId] = profile;
        }

        profile.Persona = persona;
        profile.LastUpdated = DateTime.UtcNow;

        // 載入預設的假操作事件和訂單資料
        LoadDefaultActionsForPersona(userId, personaId);
    }

    /// <summary>
    /// 為 Persona 載入預設的假操作事件和訂單資料
    /// </summary>
    private void LoadDefaultActionsForPersona(string userId, string personaId)
    {
        var profile = _userProfiles[userId];
        var now = DateTime.UtcNow;

        // 清除現有操作（如果有的話）
        profile.RecentActions.Clear();
        profile.PreferredCategories.Clear();
        profile.PreferredBrands.Clear();
        profile.PreferredKeywords.Clear();

        // 根據不同的 Persona 載入不同的假資料
        switch (personaId)
        {
            case "chef":
                // 廚師：搜尋料理相關，購買廚具、家電、餐飲相關
                LoadChefActions(profile, now);
                break;
            case "tycoon":
                // 鋼鐵人：搜尋頂級、奢華，購買高價商品、旅遊服務
                LoadTycoonActions(profile, now);
                break;
            case "commuter":
                // 上班族：搜尋防曬、美妝，購買通勤相關商品
                LoadCommuterActions(profile, now);
                break;
            case "outdoor":
                // 水上運動員：搜尋防曬、運動，購買戶外運動相關商品
                LoadOutdoorActions(profile, now);
                break;
            case "sensitive_skin":
                // 演員：搜尋物理性、防敏，購買敏感肌專用商品
                LoadSensitiveSkinActions(profile, now);
                break;
            case "headphone_seeker":
                // 耳機尋求者：從無線耳機開始，逐漸轉向降噪、通話清晰、商務耳機
                LoadHeadphoneSeekerActions(profile, now);
                break;
        }

        // 更新偏好統計
        foreach (var action in profile.RecentActions)
        {
            UpdateUserPreferences(profile, action);
        }

        profile.LastUpdated = now;
    }

    private void LoadChefActions(UserProfile profile, DateTime baseTime)
    {
        // 搜尋操作
        profile.RecentActions.Add(new UserAction
        {
            Id = "chef-search-1",
            UserId = profile.UserId,
            PersonaId = "chef",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "料理機",
            SearchResultCount = 15,
            Timestamp = baseTime.AddDays(-10)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "chef-search-2",
            UserId = profile.UserId,
            PersonaId = "chef",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "電飯煲",
            SearchResultCount = 8,
            Timestamp = baseTime.AddDays(-8)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "chef-search-3",
            UserId = profile.UserId,
            PersonaId = "chef",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "黑白大廚",
            SearchResultCount = 3,
            Timestamp = baseTime.AddDays(-5)
        });

        // 點擊和購買操作
        profile.RecentActions.Add(new UserAction
        {
            Id = "chef-click-1",
            UserId = profile.UserId,
            PersonaId = "chef",
            ActionType = UserActionType.ClickProduct,
            ProductId = 16,
            ProductName = "多功能料理機",
            ProductCategory = "家電",
            ProductBrand = "廚神",
            Timestamp = baseTime.AddDays(-9)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "chef-cart-1",
            UserId = profile.UserId,
            PersonaId = "chef",
            ActionType = UserActionType.AddToCart,
            ProductId = 16,
            ProductName = "多功能料理機",
            ProductCategory = "家電",
            ProductBrand = "廚神",
            Timestamp = baseTime.AddDays(-9)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "chef-checkout-1",
            UserId = profile.UserId,
            PersonaId = "chef",
            ActionType = UserActionType.Checkout,
            ProductId = 16,
            ProductName = "多功能料理機",
            ProductCategory = "家電",
            ProductBrand = "廚神",
            Timestamp = baseTime.AddDays(-8)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "chef-click-2",
            UserId = profile.UserId,
            PersonaId = "chef",
            ActionType = UserActionType.ClickProduct,
            ProductId = 7,
            ProductName = "超強智能電飯煲",
            ProductCategory = "家電",
            ProductBrand = "飯煲大王",
            Timestamp = baseTime.AddDays(-7)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "chef-checkout-2",
            UserId = profile.UserId,
            PersonaId = "chef",
            ActionType = UserActionType.Checkout,
            ProductId = 7,
            ProductName = "超強智能電飯煲",
            ProductCategory = "家電",
            ProductBrand = "飯煲大王",
            Timestamp = baseTime.AddDays(-6)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "chef-checkout-3",
            UserId = profile.UserId,
            PersonaId = "chef",
            ActionType = UserActionType.Checkout,
            ProductId = 56,
            ProductName = "長榮飯店白黑大廚自助餐優惠券",
            ProductCategory = "餐飲",
            ProductBrand = "長榮飯店",
            Timestamp = baseTime.AddDays(-4)
        });
    }

    private void LoadTycoonActions(UserProfile profile, DateTime baseTime)
    {
        // 搜尋操作
        profile.RecentActions.Add(new UserAction
        {
            Id = "tycoon-search-1",
            UserId = profile.UserId,
            PersonaId = "tycoon",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "頂級手機",
            SearchResultCount = 12,
            Timestamp = baseTime.AddDays(-12)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "tycoon-search-2",
            UserId = profile.UserId,
            PersonaId = "tycoon",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "奢華旅遊",
            SearchResultCount = 8,
            Timestamp = baseTime.AddDays(-8)
        });

        // 點擊和購買操作
        profile.RecentActions.Add(new UserAction
        {
            Id = "tycoon-checkout-1",
            UserId = profile.UserId,
            PersonaId = "tycoon",
            ActionType = UserActionType.Checkout,
            ProductId = 61,
            ProductName = "蘋果 iPhone 16 Pro 智慧型手機",
            ProductCategory = "電子產品",
            ProductBrand = "蘋果",
            Timestamp = baseTime.AddDays(-10)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "tycoon-checkout-2",
            UserId = profile.UserId,
            PersonaId = "tycoon",
            ActionType = UserActionType.Checkout,
            ProductId = 33,
            ProductName = "4K 超高清投影機",
            ProductCategory = "電子產品",
            ProductBrand = "影視",
            Timestamp = baseTime.AddDays(-7)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "tycoon-checkout-3",
            UserId = profile.UserId,
            PersonaId = "tycoon",
            ActionType = UserActionType.Checkout,
            ProductId = 54,
            ProductName = "長榮飯店 3日住宿優惠券",
            ProductCategory = "旅遊",
            ProductBrand = "長榮飯店",
            Timestamp = baseTime.AddDays(-5)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "tycoon-checkout-4",
            UserId = profile.UserId,
            PersonaId = "tycoon",
            ActionType = UserActionType.Checkout,
            ProductId = 57,
            ProductName = "長榮航空國際航班升等優惠券",
            ProductCategory = "旅遊",
            ProductBrand = "長榮航空",
            Timestamp = baseTime.AddDays(-3)
        });
    }

    private void LoadCommuterActions(UserProfile profile, DateTime baseTime)
    {
        // 搜尋操作
        profile.RecentActions.Add(new UserAction
        {
            Id = "commuter-search-1",
            UserId = profile.UserId,
            PersonaId = "commuter",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "防曬 不卡粉",
            SearchResultCount = 10,
            Timestamp = baseTime.AddDays(-14)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "commuter-search-2",
            UserId = profile.UserId,
            PersonaId = "commuter",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "妝前乳 防曬",
            SearchResultCount = 6,
            Timestamp = baseTime.AddDays(-10)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "commuter-search-3",
            UserId = profile.UserId,
            PersonaId = "commuter",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "無線耳機 降噪",
            SearchResultCount = 15,
            Timestamp = baseTime.AddDays(-5)
        });

        // 點擊和購買操作
        profile.RecentActions.Add(new UserAction
        {
            Id = "commuter-click-1",
            UserId = profile.UserId,
            PersonaId = "commuter",
            ActionType = UserActionType.ClickProduct,
            ProductId = 97,
            ProductName = "通勤族清爽防曬凝乳 SPF50+",
            ProductCategory = "美妝",
            ProductBrand = "日常守護",
            Timestamp = baseTime.AddDays(-12)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "commuter-checkout-1",
            UserId = profile.UserId,
            PersonaId = "commuter",
            ActionType = UserActionType.Checkout,
            ProductId = 97,
            ProductName = "通勤族清爽防曬凝乳 SPF50+",
            ProductCategory = "美妝",
            ProductBrand = "日常守護",
            Timestamp = baseTime.AddDays(-11)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "commuter-checkout-2",
            UserId = profile.UserId,
            PersonaId = "commuter",
            ActionType = UserActionType.Checkout,
            ProductId = 98,
            ProductName = "潤色妝前防曬乳 SPF30",
            ProductCategory = "美妝",
            ProductBrand = "素顏光",
            Timestamp = baseTime.AddDays(-9)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "commuter-click-2",
            UserId = profile.UserId,
            PersonaId = "commuter",
            ActionType = UserActionType.ClickProduct,
            ProductId = 107,
            ProductName = "商務降噪藍牙耳機組",
            ProductCategory = "電子產品",
            ProductBrand = "辦公聲學",
            Timestamp = baseTime.AddDays(-4)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "commuter-checkout-3",
            UserId = profile.UserId,
            PersonaId = "commuter",
            ActionType = UserActionType.Checkout,
            ProductId = 107,
            ProductName = "商務降噪藍牙耳機組",
            ProductCategory = "電子產品",
            ProductBrand = "辦公聲學",
            Timestamp = baseTime.AddDays(-3)
        });
    }

    private void LoadOutdoorActions(UserProfile profile, DateTime baseTime)
    {
        // 搜尋操作
        profile.RecentActions.Add(new UserAction
        {
            Id = "outdoor-search-1",
            UserId = profile.UserId,
            PersonaId = "outdoor",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "防曬 防水",
            SearchResultCount = 12,
            Timestamp = baseTime.AddDays(-15)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "outdoor-search-2",
            UserId = profile.UserId,
            PersonaId = "outdoor",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "運動 防曬 長效",
            SearchResultCount = 8,
            Timestamp = baseTime.AddDays(-10)
        });

        // 點擊和購買操作
        profile.RecentActions.Add(new UserAction
        {
            Id = "outdoor-checkout-1",
            UserId = profile.UserId,
            PersonaId = "outdoor",
            ActionType = UserActionType.Checkout,
            ProductId = 99,
            ProductName = "戶外運動極效防水防曬乳 SPF50+ PA++++",
            ProductCategory = "美妝",
            ProductBrand = "戶外盾牌",
            Timestamp = baseTime.AddDays(-12)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "outdoor-checkout-2",
            UserId = profile.UserId,
            PersonaId = "outdoor",
            ActionType = UserActionType.Checkout,
            ProductId = 104,
            ProductName = "海邊戲水專用全身防曬噴霧 SPF50",
            ProductCategory = "美妝",
            ProductBrand = "海灘守護",
            Timestamp = baseTime.AddDays(-8)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "outdoor-checkout-3",
            UserId = profile.UserId,
            PersonaId = "outdoor",
            ActionType = UserActionType.Checkout,
            ProductId = 36,
            ProductName = "輕量運動水壺",
            ProductCategory = "運動用品",
            ProductBrand = "活力水",
            Timestamp = baseTime.AddDays(-6)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "outdoor-checkout-4",
            UserId = profile.UserId,
            PersonaId = "outdoor",
            ActionType = UserActionType.Checkout,
            ProductId = 37,
            ProductName = "可調節瑜伽墊",
            ProductCategory = "運動用品",
            ProductBrand = "健身派",
            Timestamp = baseTime.AddDays(-4)
        });
    }

    private void LoadSensitiveSkinActions(UserProfile profile, DateTime baseTime)
    {
        // 搜尋操作
        profile.RecentActions.Add(new UserAction
        {
            Id = "sensitive_skin-search-1",
            UserId = profile.UserId,
            PersonaId = "sensitive_skin",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "物理性 防曬",
            SearchResultCount = 5,
            Timestamp = baseTime.AddDays(-12)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "sensitive_skin-search-2",
            UserId = profile.UserId,
            PersonaId = "sensitive_skin",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "無酒精 防敏",
            SearchResultCount = 8,
            Timestamp = baseTime.AddDays(-8)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "sensitive_skin-search-3",
            UserId = profile.UserId,
            PersonaId = "sensitive_skin",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "敏感肌 保養",
            SearchResultCount = 10,
            Timestamp = baseTime.AddDays(-5)
        });

        // 點擊和購買操作
        profile.RecentActions.Add(new UserAction
        {
            Id = "sensitive_skin-checkout-1",
            UserId = profile.UserId,
            PersonaId = "sensitive_skin",
            ActionType = UserActionType.Checkout,
            ProductId = 101,
            ProductName = "敏感肌物理性防曬霜 SPF30",
            ProductCategory = "美妝",
            ProductBrand = "溫和安心",
            Timestamp = baseTime.AddDays(-10)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "sensitive_skin-checkout-2",
            UserId = profile.UserId,
            PersonaId = "sensitive_skin",
            ActionType = UserActionType.Checkout,
            ProductId = 105,
            ProductName = "醫美修復型防曬乳 SPF30",
            ProductCategory = "美妝",
            ProductBrand = "修復之光",
            Timestamp = baseTime.AddDays(-7)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "sensitive_skin-checkout-3",
            UserId = profile.UserId,
            PersonaId = "sensitive_skin",
            ActionType = UserActionType.Checkout,
            ProductId = 5,
            ProductName = "超級保濕面膜",
            ProductCategory = "美妝",
            ProductBrand = "保濕女王",
            Timestamp = baseTime.AddDays(-4)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "sensitive_skin-checkout-4",
            UserId = profile.UserId,
            PersonaId = "sensitive_skin",
            ActionType = UserActionType.Checkout,
            ProductId = 11,
            ProductName = "極致保濕面膜",
            ProductCategory = "美妝",
            ProductBrand = "水潤女神",
            Timestamp = baseTime.AddDays(-2)
        });
    }

    private void LoadHeadphoneSeekerActions(UserProfile profile, DateTime baseTime)
    {
        // 第一階段：搜尋「無線耳機」（初始需求）
        profile.RecentActions.Add(new UserAction
        {
            Id = "headphone_seeker-search-1",
            UserId = profile.UserId,
            PersonaId = "headphone_seeker",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "無線耳機",
            SearchResultCount = 20,
            Timestamp = baseTime.AddDays(-15)
        });

        // 點擊無線耳機，但開始關注降噪功能
        profile.RecentActions.Add(new UserAction
        {
            Id = "headphone_seeker-click-1",
            UserId = profile.UserId,
            PersonaId = "headphone_seeker",
            ActionType = UserActionType.ClickProduct,
            ProductId = 15,
            ProductName = "無線藍牙耳機",
            ProductCategory = "電子產品",
            ProductBrand = "音樂達人",
            Timestamp = baseTime.AddDays(-14)
        });

        // 第二階段：搜尋「降噪耳機」（開始關注降噪）
        profile.RecentActions.Add(new UserAction
        {
            Id = "headphone_seeker-search-2",
            UserId = profile.UserId,
            PersonaId = "headphone_seeker",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "降噪耳機",
            SearchResultCount = 12,
            Timestamp = baseTime.AddDays(-12)
        });

        // 點擊降噪耳機
        profile.RecentActions.Add(new UserAction
        {
            Id = "headphone_seeker-click-2",
            UserId = profile.UserId,
            PersonaId = "headphone_seeker",
            ActionType = UserActionType.ClickProduct,
            ProductId = 32,
            ProductName = "無線降噪耳機",
            ProductCategory = "電子產品",
            ProductBrand = "靜音",
            Timestamp = baseTime.AddDays(-11)
        });

        // 第三階段：搜尋「通話清晰 耳機」（開始關注通話品質）
        profile.RecentActions.Add(new UserAction
        {
            Id = "headphone_seeker-search-3",
            UserId = profile.UserId,
            PersonaId = "headphone_seeker",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "通話清晰 耳機",
            SearchResultCount = 8,
            Timestamp = baseTime.AddDays(-9)
        });

        // 點擊商務耳機（關注通話功能）
        profile.RecentActions.Add(new UserAction
        {
            Id = "headphone_seeker-click-3",
            UserId = profile.UserId,
            PersonaId = "headphone_seeker",
            ActionType = UserActionType.ClickProduct,
            ProductId = 110,
            ProductName = "遠端工作專用會議耳機",
            ProductCategory = "電子產品",
            ProductBrand = "遠距聲線",
            Timestamp = baseTime.AddDays(-8)
        });

        // 點擊混合降噪耳機（降噪 + 會議）
        profile.RecentActions.Add(new UserAction
        {
            Id = "headphone_seeker-click-4",
            UserId = profile.UserId,
            PersonaId = "headphone_seeker",
            ActionType = UserActionType.ClickProduct,
            ProductId = 111,
            ProductName = "Hybrid ANC 音樂與會議兩用耳機",
            ProductCategory = "電子產品",
            ProductBrand = "雙模聲學",
            Timestamp = baseTime.AddDays(-7)
        });

        // 第四階段：購買「商務降噪藍牙耳機組」（關鍵轉折點）
        profile.RecentActions.Add(new UserAction
        {
            Id = "headphone_seeker-click-5",
            UserId = profile.UserId,
            PersonaId = "headphone_seeker",
            ActionType = UserActionType.ClickProduct,
            ProductId = 107,
            ProductName = "商務降噪藍牙耳機組",
            ProductCategory = "電子產品",
            ProductBrand = "辦公聲學",
            Timestamp = baseTime.AddDays(-6)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "headphone_seeker-cart-1",
            UserId = profile.UserId,
            PersonaId = "headphone_seeker",
            ActionType = UserActionType.AddToCart,
            ProductId = 107,
            ProductName = "商務降噪藍牙耳機組",
            ProductCategory = "電子產品",
            ProductBrand = "辦公聲學",
            Timestamp = baseTime.AddDays(-6)
        });

        profile.RecentActions.Add(new UserAction
        {
            Id = "headphone_seeker-checkout-1",
            UserId = profile.UserId,
            PersonaId = "headphone_seeker",
            ActionType = UserActionType.Checkout,
            ProductId = 107,
            ProductName = "商務降噪藍牙耳機組",
            ProductCategory = "電子產品",
            ProductBrand = "辦公聲學",
            Timestamp = baseTime.AddDays(-5)
        });

        // 第五階段：購買後，搜尋更多商務/通勤相關耳機
        profile.RecentActions.Add(new UserAction
        {
            Id = "headphone_seeker-search-4",
            UserId = profile.UserId,
            PersonaId = "headphone_seeker",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "通勤 降噪 耳機",
            SearchResultCount = 6,
            Timestamp = baseTime.AddDays(-4)
        });

        // 點擊通勤用降噪耳機
        profile.RecentActions.Add(new UserAction
        {
            Id = "headphone_seeker-click-6",
            UserId = profile.UserId,
            PersonaId = "headphone_seeker",
            ActionType = UserActionType.ClickProduct,
            ProductId = 108,
            ProductName = "通勤用主動降噪耳罩式耳機",
            ProductCategory = "電子產品",
            ProductBrand = "通勤聲學",
            Timestamp = baseTime.AddDays(-3)
        });

        // 搜尋「會議 麥克風 耳機」
        profile.RecentActions.Add(new UserAction
        {
            Id = "headphone_seeker-search-5",
            UserId = profile.UserId,
            PersonaId = "headphone_seeker",
            ActionType = UserActionType.SearchQuery,
            SearchQuery = "會議 麥克風 耳機",
            SearchResultCount = 5,
            Timestamp = baseTime.AddDays(-2)
        });

        // 點擊多裝置切換商務耳機
        profile.RecentActions.Add(new UserAction
        {
            Id = "headphone_seeker-click-7",
            UserId = profile.UserId,
            PersonaId = "headphone_seeker",
            ActionType = UserActionType.ClickProduct,
            ProductId = 109,
            ProductName = "多裝置切換商務藍牙耳機",
            ProductCategory = "電子產品",
            ProductBrand = "會議好夥伴",
            Timestamp = baseTime.AddDays(-1)
        });

        // 點擊運動耳機（具通話降噪）
        profile.RecentActions.Add(new UserAction
        {
            Id = "headphone_seeker-click-8",
            UserId = profile.UserId,
            PersonaId = "headphone_seeker",
            ActionType = UserActionType.ClickProduct,
            ProductId = 112,
            ProductName = "輕量運動藍牙耳機（具通話降噪）",
            ProductCategory = "電子產品",
            ProductBrand = "動能聲學",
            Timestamp = baseTime.AddDays(-1)
        });
    }

    /// <summary>
    /// 記錄用戶操作
    /// </summary>
    public void RecordUserAction(UserAction action)
    {
        if (!_userProfiles.TryGetValue(action.UserId, out var profile))
        {
            profile = new UserProfile { UserId = action.UserId };
            _userProfiles[action.UserId] = profile;
        }

        // 添加到操作歷史（保留最近 100 筆）
        profile.RecentActions.Add(action);
        if (profile.RecentActions.Count > 100)
        {
            profile.RecentActions.RemoveAt(0);
        }

        // 更新偏好統計
        UpdateUserPreferences(profile, action);

        profile.LastUpdated = DateTime.UtcNow;
    }

    /// <summary>
    /// 更新用戶偏好統計
    /// </summary>
    private void UpdateUserPreferences(UserProfile profile, UserAction action)
    {
        // 根據操作類型更新偏好
        switch (action.ActionType)
        {
            case UserActionType.ViewProduct:
            case UserActionType.ClickProduct:
            case UserActionType.AddToCart:
            case UserActionType.Checkout:
                if (!string.IsNullOrEmpty(action.ProductCategory))
                {
                    profile.PreferredCategories.TryGetValue(action.ProductCategory, out var categoryCount);
                    profile.PreferredCategories[action.ProductCategory] = categoryCount + 1;
                }

                if (!string.IsNullOrEmpty(action.ProductBrand))
                {
                    profile.PreferredBrands.TryGetValue(action.ProductBrand, out var brandCount);
                    profile.PreferredBrands[action.ProductBrand] = brandCount + 1;
                }
                break;

            case UserActionType.SearchQuery:
                if (!string.IsNullOrEmpty(action.SearchQuery))
                {
                    // 簡單的關鍵字提取（可以改進為更複雜的 NLP 處理）
                    var keywords = action.SearchQuery.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                    foreach (var keyword in keywords)
                    {
                        profile.PreferredKeywords.TryGetValue(keyword, out var keywordCount);
                        profile.PreferredKeywords[keyword] = keywordCount + 1;
                    }
                }
                break;
        }
    }

    /// <summary>
    /// 取得用戶檔案
    /// </summary>
    public UserProfile? GetUserProfile(string userId)
    {
        return _userProfiles.TryGetValue(userId, out var profile) ? profile : null;
    }

    /// <summary>
    /// 生成個人化搜尋向量
    /// 結合 Persona 描述和用戶行為，生成用於向量搜尋的個人化向量
    /// </summary>
    public async Task<float[]?> GeneratePersonalizedSearchVectorAsync(string userId, string? baseSearchText = null)
    {
        var profile = GetUserProfile(userId);
        if (profile == null)
        {
            return null;
        }

        // 構建個人化搜尋文字
        var personalizedText = new List<string>();

        // 1. 加入 Persona 描述
        if (profile.Persona != null)
        {
            personalizedText.Add(profile.Persona.Description);
            // personalizedText.AddRange(profile.Persona.PreferredKeywords);
        }

        // 2. 加入用戶行為偏好（從最近的操作中提取）
        var recentProductActions = profile.RecentActions
            .Where(a => a.ProductName != null)
            .OrderByDescending(a => a.Timestamp)
            .Take(10)
            .ToList();

        foreach (var action in recentProductActions)
        {
            if (!string.IsNullOrEmpty(action.ProductName))
            {
                personalizedText.Add(action.ProductName);
            }
            if (!string.IsNullOrEmpty(action.ProductCategory))
            {
                personalizedText.Add(action.ProductCategory);
            }
        }

        // // 3. 加入搜尋關鍵字偏好
        // var topKeywords = profile.PreferredKeywords
        //     .OrderByDescending(kvp => kvp.Value)
        //     .Take(5)
        //     .Select(kvp => kvp.Key);

        // personalizedText.AddRange(topKeywords);

        // 4. 加入基礎搜尋文字（如果有的話）
        if (!string.IsNullOrEmpty(baseSearchText))
        {
            personalizedText.Add(baseSearchText);
        }

        // 生成向量
        var combinedText = string.Join(" ", personalizedText);
        if (string.IsNullOrWhiteSpace(combinedText))
        {
            return null;
        }

        try
        {
            var embeddingResult = await _embeddingClient.GenerateEmbeddingAsync(combinedText);
            var vector = embeddingResult.Value.ToFloats().ToArray();
            profile.PreferenceVector = vector;
            return vector;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate personalized search vector for user {UserId}", userId);
            return null;
        }
    }

    /// <summary>
    /// 取得用戶的推薦分類（用於 scrolling profile）
    /// </summary>
    public List<string> GetRecommendedCategories(string userId)
    {
        var profile = GetUserProfile(userId);
        if (profile == null)
        {
            return new List<string>();
        }

        var categories = new HashSet<string>();

        // 從 Persona 加入
        if (profile.Persona != null)
        {
            foreach (var category in profile.Persona.PreferredCategories)
            {
                categories.Add(category);
            }
        }

        // 從用戶行為加入（取前 5 個最常互動的分類）
        var topCategories = profile.PreferredCategories
            .OrderByDescending(kvp => kvp.Value)
            .Take(5)
            .Select(kvp => kvp.Key);

        foreach (var category in topCategories)
        {
            categories.Add(category);
        }

        return categories.ToList();
    }

    /// <summary>
    /// 取得特定 Persona 的所有訂單（Checkout 操作）
    /// </summary>
    public List<UserAction> GetPersonaOrders(string personaId)
    {
        // 創建一個臨時用戶 ID 來載入 Persona 的假資料
        var tempUserId = $"temp_{personaId}";
        
        // 如果還沒有載入過，先載入假資料
        if (!_userProfiles.TryGetValue(tempUserId, out var profile))
        {
            SetUserPersona(tempUserId, personaId);
            profile = GetUserProfile(tempUserId);
        }

        if (profile == null)
        {
            return new List<UserAction>();
        }

        // 返回所有 Checkout 操作
        return profile.RecentActions
            .Where(a => a.ActionType == UserActionType.Checkout)
            .OrderByDescending(a => a.Timestamp)
            .ToList();
    }

    /// <summary>
    /// 取得特定 Persona 的所有事件（所有操作類型）
    /// </summary>
    public List<UserAction> GetPersonaEvents(string personaId)
    {
        // 創建一個臨時用戶 ID 來載入 Persona 的假資料
        var tempUserId = $"temp_{personaId}";
        
        // 如果還沒有載入過，先載入假資料
        if (!_userProfiles.TryGetValue(tempUserId, out var profile))
        {
            SetUserPersona(tempUserId, personaId);
            profile = GetUserProfile(tempUserId);
        }

        if (profile == null)
        {
            return new List<UserAction>();
        }

        // 返回所有操作，按時間排序
        return profile.RecentActions
            .OrderByDescending(a => a.Timestamp)
            .ToList();
    }

    /// <summary>
    /// 根據選擇的 Persona、訂單和事件生成個人化搜尋向量
    /// </summary>
    public async Task<float[]?> GeneratePersonalizedSearchVectorFromSelectionAsync(
        string? personaId,
        List<string>? selectedOrderIds,
        List<string>? selectedEventIds,
        string? baseSearchText = null)
    {
        // 構建個人化搜尋文字
        var personalizedText = new List<string>();

        // 1. 加入 Persona 描述（如果選擇了 Persona）
        if (!string.IsNullOrEmpty(personaId))
        {
            var persona = GetPersona(personaId);
            if (persona != null)
            {
                personalizedText.Add(persona.Occupation);
                personalizedText.Add(persona.Description);
                // personalizedText.AddRange(persona.PreferredKeywords);
            }
        }

        // 2. 加入選擇的訂單資訊
        if (!string.IsNullOrEmpty(personaId) && selectedOrderIds != null && selectedOrderIds.Any())
        {
            var orders = GetPersonaOrders(personaId);
            var selectedOrders = orders.Where(o => selectedOrderIds.Contains(o.Id)).ToList();
            
            foreach (var order in selectedOrders)
            {
                if (!string.IsNullOrEmpty(order.ProductName))
                {
                    personalizedText.Add(order.ProductName);
                }
                if (!string.IsNullOrEmpty(order.ProductCategory))
                {
                    personalizedText.Add(order.ProductCategory);
                }
            }
        }

        // 3. 加入選擇的事件資訊
        if (!string.IsNullOrEmpty(personaId) && selectedEventIds != null && selectedEventIds.Any())
        {
            var events = GetPersonaEvents(personaId);
            var selectedEvents = events.Where(e => selectedEventIds.Contains(e.Id)).ToList();
            
            foreach (var evt in selectedEvents)
            {
                if (!string.IsNullOrEmpty(evt.ProductName))
                {
                    personalizedText.Add(evt.ProductName);
                }
                if (!string.IsNullOrEmpty(evt.ProductCategory))
                {
                    personalizedText.Add(evt.ProductCategory);
                }
                if (!string.IsNullOrEmpty(evt.SearchQuery))
                {
                    personalizedText.Add(evt.SearchQuery);
                }
            }
        }

        // 4. 加入基礎搜尋文字（如果有的話）
        if (!string.IsNullOrEmpty(baseSearchText))
        {
            personalizedText.Add(baseSearchText);
        }

        // 生成向量
        var combinedText = string.Join(" ", personalizedText);
        if (string.IsNullOrWhiteSpace(combinedText))
        {
            return null;
        }

        try
        {
            var embeddingResult = await _embeddingClient.GenerateEmbeddingAsync(combinedText);
            return embeddingResult.Value.ToFloats().ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate personalized search vector from selection");
            return null;
        }
    }
}

