using System.ComponentModel;
using Azure.AI.OpenAI;
using Azure.Identity;
using Azure.Search.Documents.Models;
using Microsoft.Agents.AI;
using Microsoft.Agents.AI.OpenAI;
using Microsoft.Agents.AI.Workflows;
using Microsoft.Extensions.DependencyInjection;
using StrongBuy.Blazor.Models;
using System.Text.Json;
using Microsoft.Extensions.AI;
using OpenAI;
using OpenAI.Embeddings;

namespace StrongBuy.Blazor.Services;

/// <summary>
/// Agent Service
/// 使用 Microsoft Agent Framework 來管理 agent、function 和 workflow
/// 用於商品搜尋個人化的加強
/// </summary>
public class AgentService
{
    private readonly AzureOpenAIClient _openAIClient;
    private readonly PersonaService _personaService;
    private readonly AzureSearchService _azureSearchService;
    private readonly EmbeddingClient _embeddingClient;
    private readonly ILogger<AgentService> _logger;
    private readonly IWebHostEnvironment _environment;
    private AIAgent? _agent;
    private AIAgent? _searchAgent; // 搜尋專用的 Agent（包含搜尋相關的 tools）
    private ProductInventory? _productInventory;
    private readonly object _inventoryLock = new();
    private Workflow? _searchWorkflow;

    public AgentService(
        [FromKeyedServices("Agent")] AzureOpenAIClient openAIClient,
        PersonaService personaService,
        AzureSearchService azureSearchService,
        EmbeddingClient embeddingClient,
        ILogger<AgentService> logger,
        IWebHostEnvironment environment)
    {
        _openAIClient = openAIClient;
        _personaService = personaService;
        _azureSearchService = azureSearchService;
        _embeddingClient = embeddingClient;
        _logger = logger;
        _environment = environment;
    }

    /// <summary>
    /// 初始化 Agent
    /// 根據快速開始指南：使用 AzureOpenAIClient 建立 Agent
    /// </summary>
    public async Task InitializeAsync()
    {
        try
        {
            // 根據快速開始指南，直接使用 AzureOpenAIClient 建立 Agent
            // 注意：這裡使用 ApiKeyCredential（已在 Program.cs 中設定）
            // 快速開始指南範例：
            // AIAgent agent = new AzureOpenAIClient(
            //   new Uri("https://your-resource.openai.azure.com/"),
            //   new AzureCliCredential())
            //     .GetChatClient("gpt-4o-mini")
            //     .CreateAIAgent(instructions: "You are good at telling jokes.");

            // 建立 Chat Client（用於一般 Agent 模式）
            _agent = _openAIClient
                .GetChatClient("gpt-4o-mini")
                .CreateAIAgent(
                    instructions:
                    "你是一個商品搜尋個人化助手。你的任務是幫助用戶找到最符合他們需求的商品。" +
                    "你可以使用提供的工具來獲取熱門關鍵字、分析用戶偏好、推薦搜尋意圖的分類或品牌，並提供個人化的搜尋建議。" +
                    "請使用繁體中文回應。",
                    tools: new[]
                    {
                        AIFunctionFactory.Create(GetTrendingKeywordsFunction),
                        AIFunctionFactory.Create(AnalyzeCategoryFunction),
                        AIFunctionFactory.Create(AnalyzeBrandFunction)
                    });

            // 建立搜尋專用的 Agent（包含搜尋相關的 tools）
            _searchAgent = _openAIClient
                .GetChatClient("gpt-4o-mini")
                .CreateAIAgent(
                    instructions:
                    "你是一個商品搜尋專家。你的任務是根據用戶的搜尋需求，使用提供的工具來執行商品搜尋。" +
                    "你可以使用以下工具：\n" +
                    "1. AnalyzeCategoryFunction - 分析搜尋關鍵字，找出相關的商品分類\n" +
                    "2. AnalyzeBrandFunction - 分析搜尋關鍵字，找出相關的品牌\n" +
                    "3. GetProductVectorFunction - 根據搜尋關鍵字和分類生成產品向量\n" +
                    "4. GetUserVectorFunction - 根據用戶 Persona 和行為生成個人化向量\n" +
                    "5. ExecuteHybridSearchFunction - 執行混合搜尋（關鍵字 + 向量）\n" +
                    "6. ExecuteTwoStageHybridSearchFunction - 執行兩階段混合搜尋（產品向量過濾 + 用戶向量排序）\n\n" +
                    "請根據搜尋需求，自行決定使用哪些工具以及使用順序。\n" +
                    "如果有用戶 Persona 信息，優先使用個人化搜尋。\n" +
                    "最後，請以 JSON 格式回傳搜尋結果，格式：{\"success\": true, \"totalCount\": 數量, \"results\": [...]}。\n" +
                    "請使用繁體中文回應。",
                    tools: new[]
                    {
                        AIFunctionFactory.Create(AnalyzeCategoryFunction),
                        AIFunctionFactory.Create(AnalyzeBrandFunction),
                        AIFunctionFactory.Create(GetProductVectorFunction),
                        AIFunctionFactory.Create(GetUserVectorFunction),
                        AIFunctionFactory.Create(ExecuteHybridSearchFunction),
                        AIFunctionFactory.Create(ExecuteTwoStageHybridSearchFunction)
                    });

            _logger.LogInformation("Agent initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Agent. Error: {Error}", ex.Message);
            // 暫時不拋出異常，讓應用程式可以繼續運行
            // 之後可以根據實際 API 調整
        }
    }

    /// <summary>
    /// GetTrendingKeywords Function
    /// 回傳熱門關鍵字列表
    /// </summary>
    [Description("Get the trending keywords.")]
    private async Task<string> GetTrendingKeywordsFunction(string argumentsJson)
    {
        try
        {
            _logger.LogInformation("GetTrendingKeywords function called with arguments: {Arguments}", argumentsJson);

            // 解析參數（如果需要）
            var arguments = JsonSerializer.Deserialize<JsonElement>(argumentsJson);

            // 從 PersonaService 取得所有用戶的搜尋關鍵字統計
            var allPersonas = _personaService.GetAllPersonas();

            // 收集所有 Persona 的偏好關鍵字
            var preferredKeywords = new List<string>();
            foreach (var persona in allPersonas)
            {
                preferredKeywords.AddRange(persona.PreferredKeywords);
            }

            // 加入一些預設的熱門關鍵字
            var defaultKeywords = new List<string>
            {
                "防曬", "保養", "美妝", "廚具", "食材", "運動", "戶外", "電子產品",
                "降噪", "通話", "商務", "專業", "高品質", "奢華", "頂級", "溫和",
                "防汗", "防水", "長效", "輕薄", "不卡粉", "物理性", "防敏", "無酒精"
            };
            defaultKeywords = ["防災", "備戰"];

            preferredKeywords.AddRange(defaultKeywords);

            // 去重並排序
            var uniqueKeywords = preferredKeywords
                .Distinct()
                //.OrderBy(k => k)
                //.Take(20) // 取前 20 個
                .ToList();

            // 回傳 JSON 格式的結果
            var result = new
            {
                keywords = uniqueKeywords,
                count = uniqueKeywords.Count,
                message = "成功取得熱門關鍵字"
            };

            var jsonResult = JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });

            _logger.LogInformation("GetTrendingKeywords function returned {Count} keywords: {Keywords}", uniqueKeywords.Count, string.Join(", ", uniqueKeywords));
            return jsonResult;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetTrendingKeywords function");
            return JsonSerializer.Serialize(new
            {
                keywords = new List<string>(),
                count = 0,
                message = $"錯誤：{ex.Message}"
            }, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
    }

    /// <summary>
    /// 執行 Agent（執行 workflow）
    /// </summary>
    public async Task<string> RunAgentAsync(string userQuery)
    {
        if (_agent == null)
        {
            await InitializeAsync();
        }

        if (_agent == null)
        {
            // 如果 Agent 初始化失敗，回傳一個基本的回應
            return "抱歉，Agent 服務目前無法使用。請稍後再試。";
        }

        try
        {
            _logger.LogInformation("Running agent with query: {Query}", userQuery);

            // 執行 agent，RunAsync 回傳 AgentRunResponse
            var result = await _agent.RunAsync(userQuery);

            _logger.LogInformation("Agent completed successfully");

            // 從 AgentRunResponse 中取得文字回應
            return result.Text ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error running agent");
            return $"執行 Agent 時發生錯誤：{ex.Message}";
        }
    }

    /// <summary>
    /// 取得 Agent 實例（用於直接操作）
    /// </summary>
    public AIAgent? GetAgent()
    {
        return _agent;
    }

    /// <summary>
    /// 取得搜尋專用的 Agent 實例
    /// </summary>
    public AIAgent? GetSearchAgent()
    {
        return _searchAgent;
    }

    /// <summary>
    /// 取得熱門關鍵字（直接呼叫 function，不透過 agent）
    /// </summary>
    public async Task<List<string>> GetTrendingKeywordsAsync()
    {
        try
        {
            var resultJson = await GetTrendingKeywordsFunction("{}");
            var result = JsonSerializer.Deserialize<JsonElement>(resultJson);

            if (result.TryGetProperty("keywords", out var keywordsElement))
            {
                var keywords = keywordsElement.EnumerateArray()
                    .Select(k => k.GetString() ?? string.Empty)
                    .Where(k => !string.IsNullOrEmpty(k))
                    .ToList();
                return keywords;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting trending keywords");
        }

        return new List<string>();
    }

    #region Product Inventory Management

    /// <summary>
    /// 載入 product_inventory.json，取得 categories 和 brands
    /// </summary>
    public ProductInventory LoadProductInventory()
    {
        if (_productInventory != null)
        {
            return _productInventory;
        }

        lock (_inventoryLock)
        {
            if (_productInventory != null)
            {
                return _productInventory;
            }

            try
            {
                var jsonPath = Path.Combine(_environment.ContentRootPath, "Data", "product_inventory.json");
                if (!File.Exists(jsonPath))
                {
                    _logger.LogWarning("Product inventory file not found: {Path}", jsonPath);
                    _productInventory = new ProductInventory();
                    return _productInventory;
                }

                var jsonString = File.ReadAllText(jsonPath);
                _productInventory = JsonSerializer.Deserialize<ProductInventory>(jsonString, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? new ProductInventory();

                _logger.LogInformation("Product inventory loaded: {CategoryCount} categories, {BrandCount} brand mappings",
                    _productInventory.Categories.Count,
                    _productInventory.BrandsByCategory.Count);

                return _productInventory;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to load product inventory");
                _productInventory = new ProductInventory();
                return _productInventory;
            }
        }
    }

    /// <summary>
    /// 取得所有分類列表
    /// </summary>
    public List<string> GetCategories()
    {
        var inventory = LoadProductInventory();
        return inventory.Categories.Select(c => c.Name).ToList();
    }

    /// <summary>
    /// 取得所有品牌列表
    /// </summary>
    public List<string> GetBrands()
    {
        var inventory = LoadProductInventory();
        var allBrands = new HashSet<string>();
        foreach (var brands in inventory.BrandsByCategory.Values)
        {
            foreach (var brand in brands)
            {
                allBrands.Add(brand);
            }
        }

        return allBrands.ToList();
    }

    /// <summary>
    /// 根據分類取得品牌列表
    /// </summary>
    public List<string> GetBrandsByCategory(string category)
    {
        var inventory = LoadProductInventory();
        return inventory.BrandsByCategory.TryGetValue(category, out var brands)
            ? brands
            : new List<string>();
    }

    #endregion

    #region Agent Functions for Analysis

    /// <summary>
    /// AnalyzeCategory Function
    /// 從搜尋關鍵字分析可能的分類
    /// </summary>
    [Description("Analyze the search query and suggest possible product categories. Returns a list of category names that match the search intent.")]
    private async Task<string> AnalyzeCategoryFunction(string argumentsJson)
    {
        try
        {
            _logger.LogInformation("AnalyzeCategory function called with arguments: {Arguments}", argumentsJson);

            var arguments = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
            var searchQuery = arguments.TryGetProperty("searchQuery", out var queryElement)
                ? queryElement.GetString() ?? string.Empty
                : string.Empty;

            if (string.IsNullOrEmpty(searchQuery))
            {
                return JsonSerializer.Serialize(new
                {
                    categories = new List<string>(),
                    message = "搜尋關鍵字為空"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }

            var inventory = LoadProductInventory();
            var availableCategories = inventory.Categories.Select(c => c.Name).ToList();

            // 獲取 user profile（如果有的話）
            string? personaId = null;
            if (arguments.TryGetProperty("personaId", out var personaIdElement))
            {
                personaId = personaIdElement.GetString();
            }

            var userProfile = !string.IsNullOrEmpty(personaId)
                ? _personaService.GetUserProfile(personaId)
                : null;

            // 構建 user profile 信息字符串
            var userProfileInfo = "";
            if (userProfile != null && userProfile.Persona != null)
            {
                var persona = userProfile.Persona;
                userProfileInfo = $"\n\n用戶個人化信息：\n" +
                                  $"職業/角色：{persona.Occupation}\n" +
                                  $"描述：{persona.Description}\n";

                if (persona.PreferredCategories != null && persona.PreferredCategories.Any())
                {
                    userProfileInfo += $"偏好分類：{string.Join("、", persona.PreferredCategories)}\n";
                }

                if (persona.PreferredKeywords != null && persona.PreferredKeywords.Any())
                {
                    userProfileInfo += $"偏好關鍵字：{string.Join("、", persona.PreferredKeywords)}\n";
                }

                // 從用戶行為中獲取的偏好
                if (userProfile.PreferredCategories != null && userProfile.PreferredCategories.Any())
                {
                    var topCategories = userProfile.PreferredCategories
                        .OrderByDescending(kvp => kvp.Value)
                        .Take(3)
                        .Select(kvp => kvp.Key);
                    userProfileInfo += $"用戶行為偏好分類：{string.Join("、", topCategories)}\n";
                }

                if (userProfile.PreferredKeywords != null && userProfile.PreferredKeywords.Any())
                {
                    var topKeywords = userProfile.PreferredKeywords
                        .OrderByDescending(kvp => kvp.Value)
                        .Take(5)
                        .Select(kvp => kvp.Key);
                    userProfileInfo += $"用戶行為偏好關鍵字：{string.Join("、", topKeywords)}\n";
                }
            }

            // 使用 Agent 分析（如果 Agent 已初始化）
            List<string> matchedCategories = new();
            bool agentAnalysisSuccess = false;

            if (_agent != null)
            {
                // 改進 prompt，提供更明確的指引，並加入 user profile 信息
                var analysisPrompt = $"你是商品分類專家。請根據搜尋關鍵字「{searchQuery}」，從以下分類中選出最相關的分類（最多 3 個）。{userProfileInfo}\n\n" +
                                     $"可用分類：{string.Join("、", availableCategories)}\n\n" +
                                     $"分析規則：\n" +
                                     $"1. 根據關鍵字的實際含義選擇分類\n" +
                                     $"2. 如果關鍵字是產品特性（如「防水」、「防曬」），考慮該特性最常見的產品類別\n" +
                                     $"3. 如果有用戶個人化信息，優先考慮用戶的偏好分類和關鍵字\n" +
                                     $"4. 分類名稱必須完全匹配可用分類列表中的名稱\n" +
                                     $"5. 如果無法確定，可以返回空陣列\n\n" +
                                     $"請以 JSON 格式回傳：{{\"categories\": [\"分類1\", \"分類2\"], \"reason\": \"說明原因\"}}";

                try
                {
                    _logger.LogInformation("=== [AGENT] Calling Agent in AnalyzeCategoryFunction for query: {Query} ===", searchQuery);
                    var agentResponse = await _agent.RunAsync(analysisPrompt);
                    var responseText = agentResponse.Text ?? string.Empty;
                    _logger.LogInformation("=== [AGENT] Agent response in AnalyzeCategoryFunction: {Response} ===", responseText);

                    // 嘗試從 Agent 回應中解析分類
                    matchedCategories = ParseCategoriesFromAgentResponse(responseText, availableCategories);

                    if (matchedCategories.Any())
                    {
                        agentAnalysisSuccess = true;
                        _logger.LogInformation("Agent successfully analyzed categories: {Categories}", string.Join(", ", matchedCategories));
                    }
                    else
                    {
                        _logger.LogWarning("Agent response did not contain valid categories, falling back to keyword matching");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to use agent for category analysis, falling back to simple matching");
                }
            }

            // 如果 Agent 分析失敗或沒有找到匹配，使用簡單的關鍵字匹配（fallback）
            if (!agentAnalysisSuccess || !matchedCategories.Any())
            {
                matchedCategories = availableCategories
                    .Where(c => searchQuery.Contains(c, StringComparison.OrdinalIgnoreCase) ||
                                c.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                    .Take(3)
                    .ToList();
            }

            // 如果還是沒有匹配，返回空列表（不要返回所有分類，避免後續查找錯誤）
            // 讓調用者決定如何處理空結果

            var result = new
            {
                categories = matchedCategories,
                count = matchedCategories.Count,
                message = matchedCategories.Any()
                    ? "成功分析分類"
                    : "未找到匹配的分類，請嘗試其他搜尋關鍵字"
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AnalyzeCategory function");
            return JsonSerializer.Serialize(new
            {
                categories = new List<string>(),
                count = 0,
                message = $"錯誤：{ex.Message}"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
    }

    /// <summary>
    /// AnalyzeBrand Function
    /// 從搜尋關鍵字分析可能的品牌
    /// </summary>
    [Description("Analyze the search query and suggest possible product brands. Returns a list of brand names that match the search intent.")]
    private async Task<string> AnalyzeBrandFunction(string argumentsJson)
    {
        try
        {
            _logger.LogInformation("AnalyzeBrand function called with arguments: {Arguments}", argumentsJson);

            var arguments = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
            var searchQuery = arguments.TryGetProperty("searchQuery", out var queryElement)
                ? queryElement.GetString() ?? string.Empty
                : string.Empty;
            var category = arguments.TryGetProperty("category", out var categoryElement)
                ? categoryElement.GetString()
                : null;

            if (string.IsNullOrEmpty(searchQuery))
            {
                return JsonSerializer.Serialize(new
                {
                    brands = new List<string>(),
                    message = "搜尋關鍵字為空"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }

            var inventory = LoadProductInventory();
            var availableBrands = string.IsNullOrEmpty(category)
                ? GetBrands()
                : GetBrandsByCategory(category);

            // 獲取 user profile（如果有的話）
            string? personaId = null;
            if (arguments.TryGetProperty("personaId", out var personaIdElement))
            {
                personaId = personaIdElement.GetString();
            }

            var userProfile = !string.IsNullOrEmpty(personaId)
                ? _personaService.GetUserProfile(personaId)
                : null;

            // 構建 user profile 信息字符串
            var userProfileInfo = "";
            if (userProfile != null && userProfile.Persona != null)
            {
                var persona = userProfile.Persona;
                userProfileInfo = $"\n\n用戶個人化信息：\n" +
                                  $"職業/角色：{persona.Occupation}\n" +
                                  $"描述：{persona.Description}\n";

                if (persona.PreferredKeywords != null && persona.PreferredKeywords.Any())
                {
                    userProfileInfo += $"偏好關鍵字：{string.Join("、", persona.PreferredKeywords)}\n";
                }

                // 從用戶行為中獲取的偏好品牌
                if (userProfile.PreferredBrands != null && userProfile.PreferredBrands.Any())
                {
                    var topBrands = userProfile.PreferredBrands
                        .OrderByDescending(kvp => kvp.Value)
                        .Take(5)
                        .Select(kvp => kvp.Key);
                    userProfileInfo += $"用戶行為偏好品牌：{string.Join("、", topBrands)}\n";
                }

                if (userProfile.PreferredKeywords != null && userProfile.PreferredKeywords.Any())
                {
                    var topKeywords = userProfile.PreferredKeywords
                        .OrderByDescending(kvp => kvp.Value)
                        .Take(5)
                        .Select(kvp => kvp.Key);
                    userProfileInfo += $"用戶行為偏好關鍵字：{string.Join("、", topKeywords)}\n";
                }
            }

            // 使用 Agent 分析（如果 Agent 已初始化）
            List<string> matchedBrands = new();
            bool agentAnalysisSuccess = false;

            if (_agent != null)
            {
                var analysisPrompt = $"根據以下搜尋關鍵字：「{searchQuery}」" +
                                     (string.IsNullOrEmpty(category) ? "" : $"和分類「{category}」") +
                                     $"，從以下品牌中選出最相關的品牌（最多 5 個）。{userProfileInfo}\n\n" +
                                     $"可用品牌列表：{string.Join(", ", availableBrands)}\n\n" +
                                     $"分析規則：\n" +
                                     $"1. 根據搜尋關鍵字和分類選擇相關品牌\n" +
                                     $"2. 如果有用戶個人化信息，優先考慮用戶的偏好品牌和關鍵字\n" +
                                     $"3. 品牌名稱必須完全匹配可用品牌列表中的名稱\n" +
                                     $"4. 如果無法確定，可以返回空陣列\n\n" +
                                     $"請以 JSON 格式回傳，格式如下：{{\"brands\": [\"品牌1\", \"品牌2\"], \"reason\": \"說明原因\"}}";

                try
                {
                    _logger.LogInformation("=== [AGENT] Calling Agent in AnalyzeBrandFunction for query: {Query} ===", searchQuery);
                    var agentResponse = await _agent.RunAsync(analysisPrompt);
                    var responseText = agentResponse.Text ?? string.Empty;
                    _logger.LogInformation("=== [AGENT] Agent response in AnalyzeBrandFunction: {Response} ===", responseText);

                    // 嘗試從 Agent 回應中解析品牌
                    matchedBrands = ParseBrandsFromAgentResponse(responseText, availableBrands);

                    if (matchedBrands.Any())
                    {
                        agentAnalysisSuccess = true;
                        _logger.LogInformation("Agent successfully analyzed brands: {Brands}", string.Join(", ", matchedBrands));
                    }
                    else
                    {
                        _logger.LogWarning("Agent response did not contain valid brands, falling back to keyword matching");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to use agent for brand analysis, falling back to simple matching");
                }
            }

            // 如果 Agent 分析失敗或沒有找到匹配，使用簡單的關鍵字匹配（fallback）
            if (!agentAnalysisSuccess || !matchedBrands.Any())
            {
                matchedBrands = availableBrands
                    .Where(b => searchQuery.Contains(b, StringComparison.OrdinalIgnoreCase) ||
                                b.Contains(searchQuery, StringComparison.OrdinalIgnoreCase))
                    .Take(5)
                    .ToList();
            }

            // 如果還是沒有匹配，返回該分類的前幾個品牌
            if (!matchedBrands.Any() && availableBrands.Any())
            {
                matchedBrands = availableBrands.Take(5).ToList();
            }

            var result = new
            {
                brands = matchedBrands,
                count = matchedBrands.Count,
                message = "成功分析品牌"
            };

            return JsonSerializer.Serialize(result, new JsonSerializerOptions
            {
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in AnalyzeBrand function");
            return JsonSerializer.Serialize(new
            {
                brands = new List<string>(),
                count = 0,
                message = $"錯誤：{ex.Message}"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
    }

    /// <summary>
    /// 從 Agent 回應中解析分類
    /// </summary>
    private List<string> ParseCategoriesFromAgentResponse(string responseText, List<string> availableCategories)
    {
        var matchedCategories = new List<string>();

        try
        {
            // 嘗試解析 JSON 格式的回應
            // 先嘗試找到 JSON 區塊（可能被其他文字包圍）
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonText = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var jsonDoc = JsonDocument.Parse(jsonText);

                // 嘗試從 "categories" 欄位取得分類
                if (jsonDoc.RootElement.TryGetProperty("categories", out var categoriesElement))
                {
                    foreach (var categoryElement in categoriesElement.EnumerateArray())
                    {
                        var categoryName = categoryElement.GetString();
                        if (!string.IsNullOrEmpty(categoryName))
                        {
                            // 與可用分類比對（不區分大小寫）
                            var matchedCategory = availableCategories.FirstOrDefault(c =>
                                string.Equals(c, categoryName, StringComparison.OrdinalIgnoreCase));

                            if (matchedCategory != null && !matchedCategories.Contains(matchedCategory))
                            {
                                matchedCategories.Add(matchedCategory);
                            }
                        }
                    }
                }
            }
            else
            {
                // 如果不是 JSON 格式，嘗試從文字中提取分類名稱
                // 檢查回應中是否包含任何可用分類的名稱
                foreach (var category in availableCategories)
                {
                    // 檢查分類名稱是否出現在回應中（不區分大小寫）
                    if (responseText.Contains(category, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!matchedCategories.Contains(category))
                        {
                            matchedCategories.Add(category);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse categories from agent response: {Response}", responseText);
        }

        // 限制最多返回 3 個分類
        return matchedCategories.Take(3).ToList();
    }

    /// <summary>
    /// 從 Agent 回應中解析品牌
    /// </summary>
    private List<string> ParseBrandsFromAgentResponse(string responseText, List<string> availableBrands)
    {
        var matchedBrands = new List<string>();

        try
        {
            // 嘗試解析 JSON 格式的回應
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonText = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var jsonDoc = JsonDocument.Parse(jsonText);

                // 嘗試從 "brands" 欄位取得品牌
                if (jsonDoc.RootElement.TryGetProperty("brands", out var brandsElement))
                {
                    foreach (var brandElement in brandsElement.EnumerateArray())
                    {
                        var brandName = brandElement.GetString();
                        if (!string.IsNullOrEmpty(brandName))
                        {
                            // 與可用品牌比對（不區分大小寫）
                            var matchedBrand = availableBrands.FirstOrDefault(b =>
                                string.Equals(b, brandName, StringComparison.OrdinalIgnoreCase));

                            if (matchedBrand != null && !matchedBrands.Contains(matchedBrand))
                            {
                                matchedBrands.Add(matchedBrand);
                            }
                        }
                    }
                }
            }
            else
            {
                // 如果不是 JSON 格式，嘗試從文字中提取品牌名稱
                foreach (var brand in availableBrands)
                {
                    if (responseText.Contains(brand, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!matchedBrands.Contains(brand))
                        {
                            matchedBrands.Add(brand);
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse brands from agent response: {Response}", responseText);
        }

        // 限制最多返回 5 個品牌
        return matchedBrands.Take(5).ToList();
    }

    #endregion

    #region Vector Generation

    /// <summary>
    /// 從搜尋關鍵字取得 product vector（包含分類信息）
    /// </summary>
    public async Task<float[]?> GetProductVectorAsync(string searchText, List<string>? categories = null)
    {
        if (string.IsNullOrWhiteSpace(searchText))
        {
            return null;
        }

        try
        {
            // 構建包含分類信息的搜尋文字
            var searchTextWithCategories = searchText;
            if (categories != null && categories.Any())
            {
                // 將分類信息加入搜尋文字，增強向量表示
                var categoriesText = string.Join(" ", categories);
                searchTextWithCategories = $"{searchText} {categoriesText}";
                _logger.LogInformation("=== [VECTOR] Adding categories to product vector: {Categories} ===", string.Join(", ", categories));
            }

            var embeddingResult = await _embeddingClient.GenerateEmbeddingAsync(searchTextWithCategories);
            return embeddingResult.Value.ToFloats().ToArray();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate product vector for search text: {SearchText}", searchText);
            return null;
        }
    }

    /// <summary>
    /// 從 persona 取得 user vector
    /// </summary>
    public async Task<float[]?> GetUserVectorAsync(
        string? personaId,
        List<string>? selectedOrderIds = null,
        List<string>? selectedEventIds = null)
    {
        if (string.IsNullOrEmpty(personaId))
        {
            return null;
        }

        try
        {
            // 使用 PersonaService 的方法來生成使用者向量
            var userVector = await _personaService.GeneratePersonalizedSearchVectorFromSelectionAsync(
                personaId,
                selectedOrderIds,
                selectedEventIds);

            return userVector;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to generate user vector for persona: {PersonaId}", personaId);
            return null;
        }
    }

    #endregion

    #region Agent Search Tools (for Agent Mode)

    /// <summary>
    /// GetProductVector Function (for Agent)
    /// 根據搜尋關鍵字和分類生成產品向量
    /// </summary>
    [Description("Generate a product vector embedding from search text and optional categories. Returns a base64-encoded JSON array of floats.")]
    private async Task<string> GetProductVectorFunction(string argumentsJson)
    {
        try
        {
            _logger.LogInformation("GetProductVector function called with arguments: {Arguments}", argumentsJson);

            var arguments = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
            var searchText = arguments.TryGetProperty("searchText", out var textElement)
                ? textElement.GetString() ?? string.Empty
                : string.Empty;

            var categories = new List<string>();
            if (arguments.TryGetProperty("categories", out var categoriesElement))
            {
                if (categoriesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var categoryElement in categoriesElement.EnumerateArray())
                    {
                        var category = categoryElement.GetString();
                        if (!string.IsNullOrEmpty(category))
                        {
                            categories.Add(category);
                        }
                    }
                }
            }

            var vector = await GetProductVectorAsync(searchText, categories.Any() ? categories : null);

            if (vector == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    message = "無法生成產品向量"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }

            return JsonSerializer.Serialize(new
            {
                success = true,
                vector = vector,
                dimensions = vector.Length
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetProductVector function");
            return JsonSerializer.Serialize(new
            {
                success = false,
                message = $"錯誤：{ex.Message}"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
    }

    /// <summary>
    /// GetUserVector Function (for Agent)
    /// 根據用戶 Persona 和行為生成個人化向量
    /// </summary>
    [Description("Generate a personalized user vector embedding from persona ID and optional order/event IDs. Returns a base64-encoded JSON array of floats.")]
    private async Task<string> GetUserVectorFunction(string argumentsJson)
    {
        try
        {
            _logger.LogInformation("GetUserVector function called with arguments: {Arguments}", argumentsJson);

            var arguments = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
            var personaId = arguments.TryGetProperty("personaId", out var personaElement)
                ? personaElement.GetString()
                : null;

            var orderIds = new List<string>();
            if (arguments.TryGetProperty("orderIds", out var orderIdsElement))
            {
                if (orderIdsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var orderIdElement in orderIdsElement.EnumerateArray())
                    {
                        var orderId = orderIdElement.GetString();
                        if (!string.IsNullOrEmpty(orderId))
                        {
                            orderIds.Add(orderId);
                        }
                    }
                }
            }

            var eventIds = new List<string>();
            if (arguments.TryGetProperty("eventIds", out var eventIdsElement))
            {
                if (eventIdsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var eventIdElement in eventIdsElement.EnumerateArray())
                    {
                        var eventId = eventIdElement.GetString();
                        if (!string.IsNullOrEmpty(eventId))
                        {
                            eventIds.Add(eventId);
                        }
                    }
                }
            }

            var vector = await GetUserVectorAsync(
                personaId,
                orderIds.Any() ? orderIds : null,
                eventIds.Any() ? eventIds : null);

            if (vector == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    message = "無法生成用戶向量"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }

            return JsonSerializer.Serialize(new
            {
                success = true,
                vector = vector,
                dimensions = vector.Length
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in GetUserVector function");
            return JsonSerializer.Serialize(new
            {
                success = false,
                message = $"錯誤：{ex.Message}"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
    }

    /// <summary>
    /// ExecuteHybridSearch Function (for Agent)
    /// 執行混合搜尋（關鍵字 + 向量）
    /// </summary>
    [Description("Execute a hybrid search (keyword + vector) on Azure AI Search. Returns search results with products.")]
    private async Task<string> ExecuteHybridSearchFunction(string argumentsJson)
    {
        try
        {
            _logger.LogInformation("ExecuteHybridSearch function called with arguments: {Arguments}", argumentsJson);

            var arguments = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
            var searchText = arguments.TryGetProperty("searchText", out var textElement)
                ? textElement.GetString() ?? string.Empty
                : string.Empty;

            var productVector = (float[]?)null;
            if (arguments.TryGetProperty("productVector", out var vectorElement))
            {
                if (vectorElement.ValueKind == JsonValueKind.Array)
                {
                    productVector = vectorElement.EnumerateArray()
                        .Select(v => (float)v.GetDouble())
                        .ToArray();
                }
            }

            var category = arguments.TryGetProperty("category", out var categoryElement)
                ? categoryElement.GetString()
                : null;

            var categories = new List<string>();
            if (arguments.TryGetProperty("categories", out var categoriesElement))
            {
                if (categoriesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var catElement in categoriesElement.EnumerateArray())
                    {
                        var cat = catElement.GetString();
                        if (!string.IsNullOrEmpty(cat))
                        {
                            categories.Add(cat);
                        }
                    }
                }
            }

            var brands = new List<string>();
            if (arguments.TryGetProperty("brands", out var brandsElement))
            {
                if (brandsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var brandElement in brandsElement.EnumerateArray())
                    {
                        var brand = brandElement.GetString();
                        if (!string.IsNullOrEmpty(brand))
                        {
                            brands.Add(brand);
                        }
                    }
                }
            }

            var top = arguments.TryGetProperty("top", out var topElement)
                ? topElement.GetInt32()
                : 1000;

            var includeFacets = arguments.TryGetProperty("includeFacets", out var facetsElement)
                ? facetsElement.GetBoolean()
                : true;

            SearchResults<ProductV2SearchDocument> searchResults;

            var filterCategories = categories.Any() ? categories : null;
            var filterCategory = filterCategories == null ? category : null;

            searchResults = await _azureSearchService.HybridSearchAsync(
                searchText,
                productVector,
                filterCategory,
                categories: filterCategories,
                brands: brands.Any() ? brands : null,
                top: top,
                includeFacets: includeFacets
            );

            var results = searchResults.GetResults().ToList();
            var products = results.Select(r => r.Document).ToList();

            return JsonSerializer.Serialize(new
            {
                success = true,
                totalCount = searchResults.TotalCount ?? 0,
                resultsCount = results.Count,
                results = products.Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    brand = p.Brand,
                    category = p.Category,
                    price = p.Price,
                    description = p.Description
                }).ToList()
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ExecuteHybridSearch function");
            return JsonSerializer.Serialize(new
            {
                success = false,
                message = $"錯誤：{ex.Message}"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
    }

    /// <summary>
    /// ExecuteTwoStageHybridSearch Function (for Agent)
    /// 執行兩階段混合搜尋（產品向量過濾 + 用戶向量排序）
    /// </summary>
    [Description("Execute a two-stage hybrid search: first filter by product vector, then rank by user vector. Returns search results with products.")]
    private async Task<string> ExecuteTwoStageHybridSearchFunction(string argumentsJson)
    {
        try
        {
            _logger.LogInformation("ExecuteTwoStageHybridSearch function called with arguments: {Arguments}", argumentsJson);

            var arguments = JsonSerializer.Deserialize<JsonElement>(argumentsJson);
            var searchText = arguments.TryGetProperty("searchText", out var textElement)
                ? textElement.GetString() ?? string.Empty
                : string.Empty;

            var productVector = (float[]?)null;
            if (arguments.TryGetProperty("productVector", out var productVectorElement))
            {
                if (productVectorElement.ValueKind == JsonValueKind.Array)
                {
                    productVector = productVectorElement.EnumerateArray()
                        .Select(v => (float)v.GetDouble())
                        .ToArray();
                }
            }

            var userVector = (float[]?)null;
            if (arguments.TryGetProperty("userVector", out var userVectorElement))
            {
                if (userVectorElement.ValueKind == JsonValueKind.Array)
                {
                    userVector = userVectorElement.EnumerateArray()
                        .Select(v => (float)v.GetDouble())
                        .ToArray();
                }
            }

            var category = arguments.TryGetProperty("category", out var categoryElement)
                ? categoryElement.GetString()
                : null;

            var categories = new List<string>();
            if (arguments.TryGetProperty("categories", out var categoriesElement))
            {
                if (categoriesElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var catElement in categoriesElement.EnumerateArray())
                    {
                        var cat = catElement.GetString();
                        if (!string.IsNullOrEmpty(cat))
                        {
                            categories.Add(cat);
                        }
                    }
                }
            }

            var brands = new List<string>();
            if (arguments.TryGetProperty("brands", out var brandsElement))
            {
                if (brandsElement.ValueKind == JsonValueKind.Array)
                {
                    foreach (var brandElement in brandsElement.EnumerateArray())
                    {
                        var brand = brandElement.GetString();
                        if (!string.IsNullOrEmpty(brand))
                        {
                            brands.Add(brand);
                        }
                    }
                }
            }

            var top = arguments.TryGetProperty("top", out var topElement)
                ? topElement.GetInt32()
                : 1000;

            var includeFacets = arguments.TryGetProperty("includeFacets", out var facetsElement)
                ? facetsElement.GetBoolean()
                : true;

            if (productVector == null || userVector == null)
            {
                return JsonSerializer.Serialize(new
                {
                    success = false,
                    message = "產品向量和用戶向量都是必需的"
                }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
            }

            var filterCategories = categories.Any() ? categories : null;
            var filterCategory = filterCategories == null ? category : null;

            var searchResults = await _azureSearchService.TwoStageHybridSearchAsync(
                searchText,
                productVector,
                userVector,
                filterCategory,
                categories: filterCategories,
                brands: brands.Any() ? brands : null,
                top: top,
                includeFacets: includeFacets
            );

            var results = searchResults.GetResults().ToList();
            var products = results.Select(r => r.Document).ToList();

            return JsonSerializer.Serialize(new
            {
                success = true,
                totalCount = searchResults.TotalCount ?? 0,
                resultsCount = results.Count,
                results = products.Select(p => new
                {
                    id = p.Id,
                    name = p.Name,
                    brand = p.Brand,
                    category = p.Category,
                    price = p.Price,
                    description = p.Description
                }).ToList()
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ExecuteTwoStageHybridSearch function");
            return JsonSerializer.Serialize(new
            {
                success = false,
                message = $"錯誤：{ex.Message}"
            }, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });
        }
    }

    /// <summary>
    /// 使用 Agent 模式執行搜尋（讓 Agent 自行決定如何使用 tools）
    /// 目前暫時 fallback 到 Workflow 模式，因為 Agent 模式的實現還需要完善
    /// </summary>
    public async Task<SearchWorkflowResult> RunAgentSearchAsync(SearchWorkflowRequest request)
    {
        _logger.LogInformation("=== [AGENT MODE] Agent mode not fully implemented yet, falling back to workflow mode ===");
        // 暫時直接使用 Workflow 模式，避免 JSON 解析錯誤
        return await ExecuteSearchWorkflowAsync(request);
    }

    #endregion

    #region Hybrid Search Workflow

    /// <summary>
    /// 執行完整的 Hybrid Search Workflow
    /// 整合所有功能：分析分類、品牌、生成向量、執行搜尋
    /// </summary>
    public async Task<SearchWorkflowResult> ExecuteSearchWorkflowAsync(SearchWorkflowRequest request)
    {
        var result = new SearchWorkflowResult();

        try
        {
            _logger.LogInformation("Starting search workflow for query: {Query}", request.SearchText);

            // 1. 分析分類（如果沒有指定）
            if (string.IsNullOrEmpty(request.Category) && !string.IsNullOrEmpty(request.SearchText))
            {
                var categoryAnalysis = await AnalyzeCategoryFromQueryAsync(request.SearchText);
                result.SuggestedCategories = categoryAnalysis;
                // 可以選擇第一個建議的分類，或讓用戶選擇
                if (categoryAnalysis.Any())
                {
                    request.Category = categoryAnalysis.First();
                }
            }

            // 2. 分析品牌（如果沒有指定）
            if ((request.Brands == null || !request.Brands.Any()) && !string.IsNullOrEmpty(request.SearchText))
            {
                var brandAnalysis = await AnalyzeBrandFromQueryAsync(request.SearchText, request.Category);
                result.SuggestedBrands = brandAnalysis;
            }

            // 3. 生成 product vector
            result.ProductVector = await GetProductVectorAsync(request.SearchText ?? string.Empty);

            // 4. 生成 user vector
            result.UserVector = await GetUserVectorAsync(
                request.PersonaId,
                request.SelectedOrderIds,
                request.SelectedEventIds);

            // 5. 執行 hybrid search
            SearchResults<ProductV2SearchDocument> searchResults;

            // 使用多個分類（如果有的話），否則使用單一分類
            var filterCategories = request.Categories;
            var filterCategory = filterCategories == null || !filterCategories.Any()
                ? request.Category
                : null; // 如果有多個分類，category 設為 null，使用 categories 參數

            if (result.ProductVector != null && result.UserVector != null)
            {
                // 兩階段搜尋：商品向量過濾 + 使用者向量 ranking
                searchResults = await _azureSearchService.TwoStageHybridSearchAsync(
                    request.SearchText,
                    result.ProductVector,
                    result.UserVector,
                    filterCategory,
                    categories: filterCategories, // 傳遞多個分類
                    brands: request.Brands,
                    top: request.Top ?? 1000,
                    includeFacets: request.IncludeFacets
                );
            }
            else if (result.ProductVector != null)
            {
                // 單階段搜尋：只有商品向量
                searchResults = await _azureSearchService.HybridSearchAsync(
                    request.SearchText,
                    result.ProductVector,
                    filterCategory,
                    categories: filterCategories, // 傳遞多個分類
                    brands: request.Brands,
                    top: request.Top ?? 1000,
                    includeFacets: request.IncludeFacets
                );
            }
            else
            {
                // 純文字搜尋
                searchResults = await _azureSearchService.HybridSearchAsync(
                    request.SearchText,
                    null,
                    filterCategory,
                    categories: filterCategories, // 傳遞多個分類
                    brands: request.Brands,
                    top: request.Top ?? 1000,
                    includeFacets: request.IncludeFacets
                );
            }

            result.SearchResults = searchResults;
            result.Success = true;

            _logger.LogInformation("Search workflow completed successfully. Found {Count} results",
                searchResults.TotalCount ?? 0);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing search workflow");
            result.Success = false;
            result.ErrorMessage = ex.Message;
        }

        return result;
    }

    /// <summary>
    /// 從查詢分析分類（使用 Agent）
    /// </summary>
    internal async Task<List<string>> AnalyzeCategoryFromQueryAsync(string searchQuery, string? personaId = null)
    {
        try
        {
            _logger.LogInformation("=== [FUNCTION] AnalyzeCategoryFromQueryAsync called (this calls AnalyzeCategoryFunction, not Agent directly) ===");
            var query = JsonSerializer.Serialize(new { searchQuery, personaId });
            var resultJson = await AnalyzeCategoryFunction(query);
            var result = JsonSerializer.Deserialize<JsonElement>(resultJson);

            if (result.TryGetProperty("categories", out var categoriesElement))
            {
                return categoriesElement.EnumerateArray()
                    .Select(c => c.GetString() ?? string.Empty)
                    .Where(c => !string.IsNullOrEmpty(c))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing category from query");
        }

        return new List<string>();
    }

    /// <summary>
    /// 從查詢分析品牌（使用 Agent）
    /// </summary>
    internal async Task<List<string>> AnalyzeBrandFromQueryAsync(string searchQuery, string? category = null, string? personaId = null)
    {
        try
        {
            _logger.LogInformation("=== [FUNCTION] AnalyzeBrandFromQueryAsync called (this calls AnalyzeBrandFunction, not Agent directly) ===");
            var query = JsonSerializer.Serialize(new { searchQuery, category, personaId });
            var resultJson = await AnalyzeBrandFunction(query);
            var result = JsonSerializer.Deserialize<JsonElement>(resultJson);

            if (result.TryGetProperty("brands", out var brandsElement))
            {
                return brandsElement.EnumerateArray()
                    .Select(b => b.GetString() ?? string.Empty)
                    .Where(b => !string.IsNullOrEmpty(b))
                    .ToList();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error analyzing brand from query");
        }

        return new List<string>();
    }

    #endregion

    #region Workflow Implementation

    /// <summary>
    /// 初始化搜尋 Workflow
    /// </summary>
    public async Task InitializeWorkflowAsync()
    {
        try
        {
            // 確保 Agent 已初始化
            if (_agent == null)
            {
                await InitializeAsync();
            }

            if (_agent == null)
            {
                _logger.LogWarning("Agent not initialized, workflow will use fallback methods");
            }

            // 建立 Executors
            var analyzeIntentExecutor = CreateAnalyzeIntentExecutor();
            var generateVectorsExecutor = CreateGenerateVectorsExecutor();
            var executeSearchExecutor = CreateExecuteSearchExecutor();

            // 建立 Workflow
            var builder = new WorkflowBuilder(analyzeIntentExecutor);
            builder.AddEdge(analyzeIntentExecutor, generateVectorsExecutor);
            builder.AddEdge(generateVectorsExecutor, executeSearchExecutor);
            builder.WithOutputFrom(executeSearchExecutor);

            _searchWorkflow = builder.Build();

            _logger.LogInformation("Search workflow initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize search workflow");
        }
    }

    /// <summary>
    /// 建立分析搜尋意圖的 Executor（使用 Agent）
    /// </summary>
    private Executor<SearchWorkflowRequest, SearchIntent> CreateAnalyzeIntentExecutor()
    {
        return new AnalyzeIntentExecutor(this, _personaService, _logger);
    }

    /// <summary>
    /// 建立生成向量的 Executor
    /// </summary>
    private Executor<SearchIntent, SearchVectors> CreateGenerateVectorsExecutor()
    {
        return new GenerateVectorsExecutor(this, _logger);
    }

    /// <summary>
    /// 建立執行搜尋的 Executor
    /// </summary>
    private Executor<SearchVectors, SearchWorkflowResult> CreateExecuteSearchExecutor()
    {
        return new ExecuteSearchExecutor(_azureSearchService, _logger);
    }

    /// <summary>
    /// 從 Agent 回應中解析搜尋意圖
    /// </summary>
    internal SearchIntent ParseSearchIntentFromAgentResponse(string responseText, SearchWorkflowRequest originalRequest)
    {
        var intent = new SearchIntent
        {
            SearchText = originalRequest.SearchText,
            Category = originalRequest.Category, // 保留單一分類以向後兼容
            Categories = originalRequest.Categories ?? (string.IsNullOrEmpty(originalRequest.Category) ? null : new List<string> { originalRequest.Category }),
            Brands = originalRequest.Brands,
            PersonaId = originalRequest.PersonaId,
            SelectedOrderIds = originalRequest.SelectedOrderIds,
            SelectedEventIds = originalRequest.SelectedEventIds
        };

        try
        {
            // 嘗試解析 JSON
            var jsonStart = responseText.IndexOf('{');
            var jsonEnd = responseText.LastIndexOf('}');

            if (jsonStart >= 0 && jsonEnd > jsonStart)
            {
                var jsonText = responseText.Substring(jsonStart, jsonEnd - jsonStart + 1);
                var jsonDoc = JsonDocument.Parse(jsonText);

                // 優先解析 categories 陣列（多個分類）
                if (jsonDoc.RootElement.TryGetProperty("categories", out var categoriesElement))
                {
                    var matchedCategories = new List<string>();
                    var availableCategories = GetCategories();

                    foreach (var categoryElement in categoriesElement.EnumerateArray())
                    {
                        var categoryName = categoryElement.GetString();
                        if (!string.IsNullOrEmpty(categoryName))
                        {
                            // 更嚴格的匹配：必須完全匹配（不區分大小寫）
                            var matchedCategory = availableCategories.FirstOrDefault(c =>
                                string.Equals(c, categoryName, StringComparison.OrdinalIgnoreCase));
                            if (matchedCategory != null && !matchedCategories.Contains(matchedCategory))
                            {
                                matchedCategories.Add(matchedCategory);
                            }
                        }
                    }

                    if (matchedCategories.Any())
                    {
                        intent.Categories = matchedCategories;
                        intent.Category = matchedCategories.First(); // 保留第一個分類以向後兼容
                        _logger.LogInformation("=== [PARSE] Matched categories from Agent response: {Categories} ===", string.Join(", ", matchedCategories));
                    }
                }
                // 向後兼容：如果沒有 categories，嘗試解析單一 category
                else if (jsonDoc.RootElement.TryGetProperty("category", out var categoryElement))
                {
                    var categoryName = categoryElement.GetString();
                    if (!string.IsNullOrEmpty(categoryName))
                    {
                        var availableCategories = GetCategories();
                        var matchedCategory = availableCategories.FirstOrDefault(c =>
                            string.Equals(c, categoryName, StringComparison.OrdinalIgnoreCase));
                        if (matchedCategory != null)
                        {
                            intent.Category = matchedCategory;
                            intent.Categories = new List<string> { matchedCategory };
                        }
                    }
                }

                if (jsonDoc.RootElement.TryGetProperty("brands", out var brandsElement))
                {
                    var brands = new List<string>();
                    foreach (var brandElement in brandsElement.EnumerateArray())
                    {
                        var brandName = brandElement.GetString();
                        if (!string.IsNullOrEmpty(brandName))
                        {
                            brands.Add(brandName);
                        }
                    }

                    if (brands.Any())
                    {
                        intent.Brands = brands;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to parse search intent from agent response");
        }

        return intent;
    }

    /// <summary>
    /// 執行搜尋 Workflow
    /// </summary>
    public async Task<SearchWorkflowResult> RunSearchWorkflowAsync(SearchWorkflowRequest request)
    {
        try
        {
            // 確保 Workflow 已初始化
            if (_searchWorkflow == null)
            {
                await InitializeWorkflowAsync();
            }

            if (_searchWorkflow == null)
            {
                _logger.LogError("Search workflow not initialized");
                return new SearchWorkflowResult
                {
                    Success = false,
                    ErrorMessage = "Search workflow not initialized"
                };
            }

            _logger.LogInformation("=== [WORKFLOW] Starting workflow execution for query: {Query} ===", request.SearchText);

            // 執行 Workflow
            await using var run = await InProcessExecution.RunAsync(_searchWorkflow, request);

            // 收集結果
            SearchWorkflowResult? result = null;
            int executorCount = 0;
            foreach (var evt in run.NewEvents)
            {
                if (evt is ExecutorCompletedEvent completedEvent)
                {
                    executorCount++;
                    _logger.LogInformation("=== [WORKFLOW] Executor #{Count} completed: {ExecutorId} ===", executorCount, completedEvent.ExecutorId);

                    // 取得最終結果（來自 ExecuteSearchExecutor）
                    if (completedEvent.ExecutorId == "ExecuteSearchExecutor" &&
                        completedEvent.Data is SearchWorkflowResult workflowResult)
                    {
                        result = workflowResult;
                        _logger.LogInformation("=== [WORKFLOW] Workflow execution completed successfully ===");
                    }
                }
            }

            // 如果沒有從事件中取得結果，嘗試從 Workflow 輸出取得
            if (result == null)
            {
                // 可能需要從其他地方取得結果
                _logger.LogWarning("=== [WORKFLOW] No result found in workflow events, using fallback ===");
                return await ExecuteSearchWorkflowAsync(request);
            }

            _logger.LogInformation("=== [WORKFLOW] Total executors executed: {Count} ===", executorCount);
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "=== [WORKFLOW] Error running search workflow ===");
            // Fallback to direct execution
            return await ExecuteSearchWorkflowAsync(request);
        }
    }

    #endregion
}

#region Workflow Executors

/// <summary>
/// 分析搜尋意圖的 Executor
/// </summary>
internal sealed class AnalyzeIntentExecutor : Executor<SearchWorkflowRequest, SearchIntent>
{
    private readonly AgentService _agentService;
    private readonly PersonaService _personaService;
    private readonly ILogger<AgentService> _logger;

    public AnalyzeIntentExecutor(AgentService agentService, PersonaService personaService, ILogger<AgentService> logger)
        : base("AnalyzeIntentExecutor")
    {
        _agentService = agentService;
        _personaService = personaService;
        _logger = logger;
    }

    public override async ValueTask<SearchIntent> HandleAsync(
        SearchWorkflowRequest input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Analyzing search intent for query: {Query}", input.SearchText);

            var intent = new SearchIntent
            {
                SearchText = input.SearchText,
                Category = input.Category, // 保留單一分類以向後兼容
                Categories = input.Categories ?? (string.IsNullOrEmpty(input.Category) ? null : new List<string> { input.Category }),
                Brands = input.Brands,
                PersonaId = input.PersonaId,
                SelectedOrderIds = input.SelectedOrderIds,
                SelectedEventIds = input.SelectedEventIds,
                Top = input.Top,
                IncludeFacets = input.IncludeFacets
            };

            var agent = _agentService.GetAgent();

            // 獲取 user profile（如果有的話）- 在方法開始處定義，以便在 fallback 中使用
            var userProfile = !string.IsNullOrEmpty(input.PersonaId)
                ? _personaService.GetUserProfile(input.PersonaId)
                : null;

            // 構建 user profile 信息字符串
            var userProfileInfo = "";
            if (userProfile != null && userProfile.Persona != null)
            {
                var persona = userProfile.Persona;
                userProfileInfo = $"\n\n用戶個人化信息：\n" +
                                  $"職業/角色：{persona.Occupation}\n" +
                                  $"描述：{persona.Description}\n";

                if (persona.PreferredCategories != null && persona.PreferredCategories.Any())
                {
                    userProfileInfo += $"偏好分類：{string.Join("、", persona.PreferredCategories)}\n";
                }

                if (persona.PreferredKeywords != null && persona.PreferredKeywords.Any())
                {
                    userProfileInfo += $"偏好關鍵字：{string.Join("、", persona.PreferredKeywords)}\n";
                }

                // 從用戶行為中獲取的偏好
                if (userProfile.PreferredCategories != null && userProfile.PreferredCategories.Any())
                {
                    var topCategories = userProfile.PreferredCategories
                        .OrderByDescending(kvp => kvp.Value)
                        .Take(3)
                        .Select(kvp => kvp.Key);
                    userProfileInfo += $"用戶行為偏好分類：{string.Join("、", topCategories)}\n";
                }

                if (userProfile.PreferredBrands != null && userProfile.PreferredBrands.Any())
                {
                    var topBrands = userProfile.PreferredBrands
                        .OrderByDescending(kvp => kvp.Value)
                        .Take(5)
                        .Select(kvp => kvp.Key);
                    userProfileInfo += $"用戶行為偏好品牌：{string.Join("、", topBrands)}\n";
                }

                if (userProfile.PreferredKeywords != null && userProfile.PreferredKeywords.Any())
                {
                    var topKeywords = userProfile.PreferredKeywords
                        .OrderByDescending(kvp => kvp.Value)
                        .Take(5)
                        .Select(kvp => kvp.Key);
                    userProfileInfo += $"用戶行為偏好關鍵字：{string.Join("、", topKeywords)}\n";
                }
            }

            // 如果 Agent 可用，使用 Agent 分析
            if (agent != null && !string.IsNullOrEmpty(input.SearchText))
            {
                var categories = _agentService.GetCategories();
                // 改進 prompt，提供更明確的指引和範例，避免 Agent 調用 function，並加入 user profile 信息
                var analysisPrompt = $"你是商品分類專家。請分析以下搜尋查詢，並從提供的分類中選出最相關的分類（可以選多個）。{userProfileInfo}\n\n" +
                                     $"搜尋查詢：「{input.SearchText}」\n" +
                                     $"Persona ID: {input.PersonaId ?? "無"}\n\n" +
                                     $"可用分類列表：{string.Join("、", categories)}\n\n" +
                                     $"分析規則：\n" +
                                     $"1. 根據搜尋關鍵字的實際含義，選擇最相關的分類（可以選 1-3 個）\n" +
                                     $"2. 如果關鍵字是產品特性（如「防水」、「防曬」），優先考慮該特性最常見的產品類別：\n" +
                                     $"   - 「防水」通常與「服飾」、「運動」（如雨衣、防水外套）、「電子產品」（如防水手機）相關\n" +
                                     $"   - 「防曬」通常與「美妝」（如防曬乳）相關\n" +
                                     $"   - 「降噪」通常與「電子產品」（如耳機）相關\n" +
                                     $"3. 如果有用戶個人化信息，優先考慮用戶的偏好分類和關鍵字\n" +
                                     $"4. 分類名稱必須完全匹配可用分類列表中的名稱，不要自行創造分類\n" +
                                     $"5. 如果無法確定，categories 可以為空陣列\n" +
                                     $"6. 品牌分析：如果關鍵字包含品牌名稱，列出相關品牌；否則可以為空陣列\n\n" +
                                     $"請直接以 JSON 格式回傳，不要調用任何 function：\n" +
                                     $"{{\"categories\": [\"分類1\", \"分類2\"], \"brands\": [\"品牌1\"], \"useVectorSearch\": true}}\n" +
                                     $"注意：categories 是陣列，可以包含多個分類。";

                try
                {
                    _logger.LogInformation("=== [AGENT] Calling Agent #1: Analyze search intent for query: {Query} ===", input.SearchText);
                    var agentResponse = await agent.RunAsync(analysisPrompt);
                    var responseText = agentResponse.Text ?? string.Empty;
                    _logger.LogInformation("=== [AGENT] Agent #1 response received: {Response} ===", responseText);

                    // 解析 Agent 回應
                    var parsedIntent = _agentService.ParseSearchIntentFromAgentResponse(responseText, input);

                    // 如果 Agent 成功解析出分類或品牌，使用解析結果
                    if ((parsedIntent.Categories != null && parsedIntent.Categories.Any()) ||
                        !string.IsNullOrEmpty(parsedIntent.Category) ||
                        (parsedIntent.Brands != null && parsedIntent.Brands.Any()))
                    {
                        intent = parsedIntent;
                        var categoriesDisplay = intent.Categories != null && intent.Categories.Any()
                            ? string.Join(", ", intent.Categories)
                            : (string.IsNullOrEmpty(intent.Category) ? "無" : intent.Category);
                        _logger.LogInformation("=== [AGENT] Agent #1 successfully parsed intent: Categories={Categories}, Brands={Brands} ===",
                            categoriesDisplay, string.Join(", ", intent.Brands ?? new List<string>()));
                    }
                    else
                    {
                        _logger.LogWarning("=== [AGENT] Agent #1 did not return valid category or brands ===");
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "=== [AGENT] Agent #1 analysis failed, using fallback ===");
                }
            }

            // Fallback: 只有在 Agent 沒有返回有效結果時才使用簡單的關鍵字匹配
            // 避免再次調用 Agent，減少執行時間
            if ((intent.Categories == null || !intent.Categories.Any()) &&
                string.IsNullOrEmpty(intent.Category) &&
                !string.IsNullOrEmpty(input.SearchText))
            {
                _logger.LogInformation("=== [FALLBACK] Using keyword matching for category (no Agent call) ===");
                var categories = _agentService.GetCategories();
                // 簡單的關鍵字匹配，不調用 Agent（可以匹配多個）
                var matchedCategories = categories
                    .Where(c => input.SearchText.Contains(c, StringComparison.OrdinalIgnoreCase) ||
                                c.Contains(input.SearchText, StringComparison.OrdinalIgnoreCase))
                    .Take(3)
                    .ToList();

                // 如果有 user profile，優先考慮用戶的偏好分類
                if (userProfile != null && userProfile.Persona != null &&
                    userProfile.Persona.PreferredCategories != null &&
                    userProfile.Persona.PreferredCategories.Any())
                {
                    var preferredCategories = userProfile.Persona.PreferredCategories
                        .Where(c => categories.Contains(c))
                        .Take(3)
                        .ToList();
                    if (preferredCategories.Any())
                    {
                        matchedCategories = preferredCategories;
                        _logger.LogInformation("=== [FALLBACK] Using user preferred categories: {Categories} ===", string.Join(", ", matchedCategories));
                    }
                }

                if (matchedCategories.Any())
                {
                    intent.Categories = matchedCategories;
                    intent.Category = matchedCategories.First(); // 保留第一個分類以向後兼容
                    _logger.LogInformation("=== [FALLBACK] Matched categories: {Categories} ===", string.Join(", ", matchedCategories));
                }
            }

            if ((intent.Brands == null || !intent.Brands.Any()) && !string.IsNullOrEmpty(input.SearchText))
            {
                _logger.LogInformation("=== [FALLBACK] Using keyword matching for brands (no Agent call) ===");
                var allBrands = _agentService.GetBrands();
                // 簡單的關鍵字匹配，不調用 Agent
                var matchedBrands = allBrands
                    .Where(b => input.SearchText.Contains(b, StringComparison.OrdinalIgnoreCase) ||
                                b.Contains(input.SearchText, StringComparison.OrdinalIgnoreCase))
                    .Take(5)
                    .ToList();

                // 如果有 user profile，優先考慮用戶的偏好品牌
                if (userProfile != null && userProfile.PreferredBrands != null &&
                    userProfile.PreferredBrands.Any())
                {
                    var preferredBrands = userProfile.PreferredBrands
                        .OrderByDescending(kvp => kvp.Value)
                        .Take(5)
                        .Select(kvp => kvp.Key)
                        .Where(b => allBrands.Contains(b))
                        .ToList();
                    if (preferredBrands.Any())
                    {
                        matchedBrands = preferredBrands;
                        _logger.LogInformation("=== [FALLBACK] Using user preferred brands: {Brands} ===", string.Join(", ", matchedBrands));
                    }
                }

                if (matchedBrands.Any())
                {
                    intent.Brands = matchedBrands;
                    _logger.LogInformation("=== [FALLBACK] Matched brands: {Brands} ===", string.Join(", ", matchedBrands));
                }
            }

            return intent;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in analyze intent executor");
            return new SearchIntent
            {
                SearchText = input.SearchText,
                Category = input.Category,
                Brands = input.Brands,
                PersonaId = input.PersonaId
            };
        }
    }
}

/// <summary>
/// 生成向量的 Executor
/// </summary>
internal sealed class GenerateVectorsExecutor : Executor<SearchIntent, SearchVectors>
{
    private readonly AgentService _agentService;
    private readonly ILogger<AgentService> _logger;

    public GenerateVectorsExecutor(AgentService agentService, ILogger<AgentService> logger)
        : base("GenerateVectorsExecutor")
    {
        _agentService = agentService;
        _logger = logger;
    }

    public override async ValueTask<SearchVectors> HandleAsync(
        SearchIntent input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Generating vectors for search intent");

            var vectors = new SearchVectors
            {
                Intent = input
            };

            // 生成 product vector
            if (!string.IsNullOrEmpty(input.SearchText))
            {
                vectors.ProductVector = await _agentService.GetProductVectorAsync(input.SearchText);
            }

            // 生成 user vector
            if (!string.IsNullOrEmpty(input.PersonaId))
            {
                vectors.UserVector = await _agentService.GetUserVectorAsync(
                    input.PersonaId,
                    input.SelectedOrderIds,
                    input.SelectedEventIds);
            }

            return vectors;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in generate vectors executor");
            return new SearchVectors { Intent = input };
        }
    }
}

/// <summary>
/// 執行搜尋的 Executor
/// </summary>
internal sealed class ExecuteSearchExecutor : Executor<SearchVectors, SearchWorkflowResult>
{
    private readonly AzureSearchService _azureSearchService;
    private readonly ILogger<AgentService> _logger;

    public ExecuteSearchExecutor(
        AzureSearchService azureSearchService,
        ILogger<AgentService> logger)
        : base("ExecuteSearchExecutor")
    {
        _azureSearchService = azureSearchService;
        _logger = logger;
    }

    public override async ValueTask<SearchWorkflowResult> HandleAsync(
        SearchVectors input,
        IWorkflowContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            _logger.LogInformation("Executing search with vectors");

            var intent = input.Intent;
            var result = new SearchWorkflowResult
            {
                // 使用多個分類（如果有的話），否則使用單一分類
                SuggestedCategories = intent.Categories ?? (string.IsNullOrEmpty(intent.Category)
                    ? new List<string>()
                    : new List<string> { intent.Category }),
                SuggestedBrands = intent.Brands ?? new List<string>(),
                ProductVector = input.ProductVector,
                UserVector = input.UserVector
            };

            // 執行搜尋
            SearchResults<ProductV2SearchDocument> searchResults;

            var top = intent.Top ?? 1000;
            var includeFacets = intent.IncludeFacets;

            // 使用多個分類作為 filter（如果有的話），否則使用單一分類
            var filterCategories = intent.Categories;
            var filterCategory = filterCategories == null || !filterCategories.Any()
                ? intent.Category
                : null; // 如果有多個分類，category 設為 null，使用 categories 參數

            if (input.ProductVector != null && input.UserVector != null)
            {
                // 兩階段搜尋
                searchResults = await _azureSearchService.TwoStageHybridSearchAsync(
                    intent.SearchText,
                    input.ProductVector,
                    input.UserVector,
                    filterCategory,
                    categories: filterCategories, // 傳遞多個分類（使用命名參數）
                    brands: intent.Brands,
                    top: top,
                    includeFacets: includeFacets
                );
            }
            else if (input.ProductVector != null)
            {
                // 單階段搜尋
                searchResults = await _azureSearchService.HybridSearchAsync(
                    intent.SearchText,
                    input.ProductVector,
                    filterCategory,
                    categories: filterCategories, // 傳遞多個分類（使用命名參數）
                    brands: intent.Brands,
                    top: top,
                    includeFacets: includeFacets
                );
            }
            else
            {
                // 純文字搜尋
                searchResults = await _azureSearchService.HybridSearchAsync(
                    intent.SearchText,
                    null,
                    filterCategory,
                    categories: filterCategories, // 傳遞多個分類（使用命名參數）
                    brands: intent.Brands,
                    top: top,
                    includeFacets: includeFacets
                );
            }

            result.SearchResults = searchResults;
            result.Success = true;

            _logger.LogInformation("Search completed. Found {Count} results",
                searchResults.TotalCount ?? 0);

            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in execute search executor");
            return new SearchWorkflowResult
            {
                Success = false,
                ErrorMessage = ex.Message
            };
        }
    }
}

#endregion

#region Workflow Models

/// <summary>
/// 搜尋 Workflow 請求
/// </summary>
public class SearchWorkflowRequest
{
    public string? SearchText { get; set; }
    public string? Category { get; set; } // 保留單一分類以向後兼容
    public List<string>? Categories { get; set; } // 新增：支援多個分類
    public List<string>? Brands { get; set; }
    public string? PersonaId { get; set; }
    public List<string>? SelectedOrderIds { get; set; }
    public List<string>? SelectedEventIds { get; set; }
    public int? Top { get; set; } = 1000;
    public bool IncludeFacets { get; set; } = true;
}

/// <summary>
/// 搜尋 Workflow 結果
/// </summary>
public class SearchWorkflowResult
{
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public List<string> SuggestedCategories { get; set; } = new();
    public List<string> SuggestedBrands { get; set; } = new();
    public float[]? ProductVector { get; set; }
    public float[]? UserVector { get; set; }
    public SearchResults<ProductV2SearchDocument>? SearchResults { get; set; }
}

/// <summary>
/// 搜尋意圖（Workflow 內部使用）
/// </summary>
public class SearchIntent
{
    public string? SearchText { get; set; }
    public string? Category { get; set; } // 保留單一分類以向後兼容（取第一個分類）
    public List<string>? Categories { get; set; } // 新增：支援多個分類
    public List<string>? Brands { get; set; }
    public string? PersonaId { get; set; }
    public List<string>? SelectedOrderIds { get; set; }
    public List<string>? SelectedEventIds { get; set; }
    public int? Top { get; set; }
    public bool IncludeFacets { get; set; } = true;
}

/// <summary>
/// 搜尋向量（Workflow 內部使用）
/// </summary>
public class SearchVectors
{
    public SearchIntent Intent { get; set; } = new();
    public float[]? ProductVector { get; set; }
    public float[]? UserVector { get; set; }
}

#endregion