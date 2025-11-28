using Azure;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
using Azure.Search.Documents.Indexes.Models;
using Azure.Search.Documents.Models;
using StrongBuy.Blazor.Models;

namespace StrongBuy.Blazor.Services;

public class AzureSearchService
{
    private readonly SearchClient _searchClient;
    private readonly SearchIndexClient _indexClient;
    private readonly ILogger<AzureSearchService> _logger;

    public AzureSearchService(SearchClient searchClient, SearchIndexClient indexClient, ILogger<AzureSearchService> logger)
    {
        _searchClient = searchClient;
        _indexClient = indexClient;
        _logger = logger;
    }

    /// <summary>
    /// 執行向量搜尋
    /// </summary>
    public async Task<SearchResults<ProductV2SearchDocument>> VectorSearchAsync(
        float[] queryVector,
        string? category = null,
        List<string>? brands = null,
        int top = 10,
        bool includeFacets = false)
    {
        // KNN 固定為 10
        const int knnCount = 10;
        
        var searchOptions = new SearchOptions
        {
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryVector)
                    {
                        KNearestNeighborsCount = knnCount,
                        Fields = { "combinedEmbedding" }
                    }
                }
            },
            Size = 1000, // 獲取所有結果（Azure AI Search 最大支援 1000）
            Select = { "*" }, // 使用 * 選擇所有欄位（包括 reviews）
            IncludeTotalCount = true
        };

        // 添加 Facets（不受 filter 影響，只受搜尋關鍵字影響）
        if (includeFacets)
        {
            searchOptions.Facets.Add("category");
            searchOptions.Facets.Add("brand");
        }

        // 添加過濾條件（用於結果，但不影響 facets）
        var filters = new List<string>();
        if (!string.IsNullOrEmpty(category))
        {
            // 轉義單引號
            var escapedCategory = category.Replace("'", "''");
            filters.Add($"category eq '{escapedCategory}'");
        }

        if (brands != null && brands.Any())
        {
            var brandFilters = brands.Select(b =>
            {
                var escapedBrand = b.Replace("'", "''");
                return $"brand eq '{escapedBrand}'";
            });
            filters.Add($"({string.Join(" or ", brandFilters)})");
        }

        if (filters.Any())
        {
            searchOptions.Filter = string.Join(" and ", filters);
        }

        // 記錄查詢詳情
        var filterString = searchOptions.Filter ?? "無";
        var vectorInfo = $"KNN={knnCount}, Field=combinedEmbedding, VectorDimensions={queryVector.Length}";
        var selectFields = string.Join(", ", searchOptions.Select);

        _logger.LogInformation(
            "[Azure AI Search] 向量搜尋查詢 | QueryType=VectorSearch | SearchText=* | Filter={Filter} | VectorSearch: KNN={KNN}, Field=combinedEmbedding, Dimensions={Dimensions} | Size={Size} | Select={Select}",
            filterString,
            knnCount,
            queryVector.Length,
            searchOptions.Size,
            selectFields
        );

        // 記錄可在 Azure Portal 使用的查詢格式
        _logger.LogInformation(
            "[Azure AI Search] Portal Query Format | SearchText: * | Filter: {Filter} | VectorSearch: KNN={KNN}, Field=combinedEmbedding",
            filterString,
            knnCount
        );

        var response = await _searchClient.SearchAsync<ProductV2SearchDocument>("*", searchOptions);

        // 記錄結果（包含 facets 和 ResultsCount）
        var facetsInfo = includeFacets && response.Value.Facets != null
            ? $"CategoryFacets={(response.Value.Facets.TryGetValue("category", out var categoryFacet) ? categoryFacet.Count : 0)}, BrandFacets={(response.Value.Facets.TryGetValue("brand", out var brandFacet) ? brandFacet.Count : 0)}"
            : "無 Facets";
        
        _logger.LogInformation(
            "[Azure AI Search] 搜尋結果 | TotalCount={TotalCount} | ResultsCount={ResultsCount} | {FacetsInfo}",
            response.Value.TotalCount ?? 0,
            response.Value.GetResults().Count(),
            facetsInfo
        );

        return response.Value;
    }

    /// <summary>
    /// 執行兩階段搜尋：商品向量用於過濾，使用者向量用於 ranking
    /// </summary>
    public async Task<SearchResults<ProductV2SearchDocument>> TwoStageHybridSearchAsync(
        string? searchText,
        float[] productVector, // 商品向量（用於過濾候選結果）
        float[] userVector,    // 使用者向量（用於 ranking）
        string? category = null,
        List<string>? brands = null,
        int top = 10,
        bool includeFacets = false)
    {
        // 第一階段：使用商品向量進行搜尋，獲取候選結果（用於過濾）
        // 候選結果數量應該等於 top，因為 userVector 只用於 reranking/boosting，不應該減少結果數量
        // 第一階段返回多少商品，最終也要返回多少商品
        const int candidateKnnCount = 10; // 使用較小的 KNN 值，只獲取最相關的候選結果
        int candidateSize = top; // 候選結果數量等於 top，確保第一階段和最終結果數量一致
        
        var candidateSearchOptions = new SearchOptions
        {
            VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(productVector)
                    {
                        KNearestNeighborsCount = candidateKnnCount,
                        Fields = { "combinedEmbedding" }
                    }
                }
            },
            Size = candidateSize, // 限制候選結果數量，只獲取最相關的結果
            Select = { "*" },
            IncludeTotalCount = true
        };

        // 啟用 Semantic Ranker（語意排序）作二次重排
        // 對多語系有效，能將錯誤極性的結果往下壓
        if (!string.IsNullOrEmpty(searchText) && searchText != "*")
        {
            candidateSearchOptions.QueryType = SearchQueryType.Semantic;
            candidateSearchOptions.SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = "default",
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                QueryAnswer = new QueryAnswer(QueryAnswerType.Extractive)
            };
        }

        // 添加過濾條件
        var filters = new List<string>();
        if (!string.IsNullOrEmpty(category))
        {
            var escapedCategory = category.Replace("'", "''");
            filters.Add($"category eq '{escapedCategory}'");
        }

        if (brands != null && brands.Any())
        {
            var brandFilters = brands.Select(b =>
            {
                var escapedBrand = b.Replace("'", "''");
                return $"brand eq '{escapedBrand}'";
            });
            filters.Add($"({string.Join(" or ", brandFilters)})");
        }

        if (filters.Any())
        {
            candidateSearchOptions.Filter = string.Join(" and ", filters);
        }

        var searchTextQuery = string.IsNullOrEmpty(searchText) ? "*" : searchText;
        var candidateResponse = await _searchClient.SearchAsync<ProductV2SearchDocument>(searchTextQuery, candidateSearchOptions);
        var candidateResults = candidateResponse.Value;

        // 如果沒有候選結果，直接返回
        if (!candidateResults.GetResults().Any())
        {
            return candidateResults;
        }

        // 第二階段：使用使用者向量對候選結果進行重新排序（在應用層）
        // 計算每個候選結果與使用者向量的相似度
        var resultsWithUserScores = candidateResults.GetResults().Select(result =>
        {
            var document = result.Document;
            
            // 計算使用者向量與商品向量的相似度（餘弦相似度）
            double userSimilarity = 0.0;
            if (document.CombinedEmbedding != null && userVector != null)
            {
                userSimilarity = CalculateCosineSimilarity(userVector, document.CombinedEmbedding);
            }
            
            // 保留原始的商品向量相似度分數（用於過濾）
            var productScore = result.Score ?? 0.0;
            
            return new
            {
                Result = result,
                Document = document,
                ProductScore = productScore, // 商品向量相似度（用於過濾）
                UserScore = userSimilarity   // 使用者向量相似度（用於 ranking）
            };
        }).ToList();

        // 根據使用者向量相似度重新排序（用於 ranking）
        // 注意：不限制結果數量，返回所有候選結果（已按 userVector 排序）
        // userVector 只用於 reranking/boosting，不應該減少結果數量
        var rankedResults = resultsWithUserScores
            .OrderByDescending(r => r.UserScore) // 主要排序：使用者向量相似度
            .ThenByDescending(r => r.ProductScore) // 次要排序：商品向量相似度
            .Select(r => r.Result)
            .ToList();

        // 注意：由於 Azure AI Search 的 SearchResults 是不可變的，我們無法直接修改結果順序
        // 排序後的結果需要在 SearchV5.razor 中處理
        // 這裡返回候選結果，實際的排序會在 SearchV5.razor 中完成
        
        // 返回候選結果，排序將在應用層完成
        return candidateResults;
    }

    /// <summary>
    /// 計算兩個向量的餘弦相似度
    /// </summary>
    private double CalculateCosineSimilarity(float[] vector1, float[] vector2)
    {
        if (vector1 == null || vector2 == null || vector1.Length != vector2.Length)
        {
            return 0.0;
        }

        double dotProduct = 0.0;
        double magnitude1 = 0.0;
        double magnitude2 = 0.0;

        for (int i = 0; i < vector1.Length; i++)
        {
            dotProduct += vector1[i] * vector2[i];
            magnitude1 += vector1[i] * vector1[i];
            magnitude2 += vector2[i] * vector2[i];
        }

        magnitude1 = Math.Sqrt(magnitude1);
        magnitude2 = Math.Sqrt(magnitude2);

        if (magnitude1 == 0.0 || magnitude2 == 0.0)
        {
            return 0.0;
        }

        return dotProduct / (magnitude1 * magnitude2);
    }

    /// <summary>
    /// 執行混合搜尋（向量 + 關鍵字）
    /// 統一使用此方法進行所有搜尋，包括有 filter 的情況
    /// </summary>
    public async Task<SearchResults<ProductV2SearchDocument>> HybridSearchAsync(
        string? searchText,
        float[]? queryVector,
        string? category = null,
        List<string>? brands = null,
        int top = 10,
        bool includeFacets = false)
    {
        // KNN 固定為 10
        const int knnCount = 10;
        
        var searchOptions = new SearchOptions
        {
            Size = 1000, // 獲取所有結果（Azure AI Search 最大支援 1000）
            Select = { "*" }, // 使用 * 選擇所有欄位（包括 reviews）
            IncludeTotalCount = true
        };

        // 添加 Facets（不受 filter 影響，只受搜尋關鍵字影響）
        if (includeFacets)
        {
            searchOptions.Facets.Add("category");
            searchOptions.Facets.Add("brand");
        }

        // 啟用 Semantic Ranker（語意排序）作二次重排
        // 對多語系有效，能將錯誤極性的結果往下壓
        if (!string.IsNullOrEmpty(searchText) && searchText != "*")
        {
            searchOptions.QueryType = SearchQueryType.Semantic;
            searchOptions.SemanticSearch = new SemanticSearchOptions
            {
                SemanticConfigurationName = "default",
                QueryCaption = new QueryCaption(QueryCaptionType.Extractive),
                QueryAnswer = new QueryAnswer(QueryAnswerType.Extractive)
            };
        }

        // 向量搜尋（如果有向量，則添加向量搜尋）
        // Hybrid 搜尋：同時使用關鍵字搜尋和向量搜尋，一次請求融合
        if (queryVector != null)
        {
            searchOptions.VectorSearch = new VectorSearchOptions
            {
                Queries =
                {
                    new VectorizedQuery(queryVector)
                    {
                        KNearestNeighborsCount = knnCount,
                        Fields = { "combinedEmbedding" }
                    }
                }
            };
        }

        // 添加過濾條件（category 和 brands）
        // Filter 會應用於搜尋結果，但不會影響 facets（facets 由單獨的查詢獲取）
        var filters = new List<string>();
        if (!string.IsNullOrEmpty(category))
        {
            // 轉義單引號
            var escapedCategory = category.Replace("'", "''");
            filters.Add($"category eq '{escapedCategory}'");
        }

        if (brands != null && brands.Any())
        {
            var brandFilters = brands.Select(b =>
            {
                var escapedBrand = b.Replace("'", "''");
                return $"brand eq '{escapedBrand}'";
            });
            filters.Add($"({string.Join(" or ", brandFilters)})");
        }

        if (filters.Any())
        {
            searchOptions.Filter = string.Join(" and ", filters);
        }

        // 文字搜尋：如果有搜尋文字則使用，否則使用 * 進行全量搜尋
        // 即使有 filter，文字搜尋仍然會參與，確保結果的相關性
        var searchTextQuery = string.IsNullOrEmpty(searchText) ? "*" : searchText;

        // 記錄查詢詳情
        var filterString = searchOptions.Filter ?? "無";
        var queryType = queryVector != null ? "HybridSearch (Vector + Text)" : "TextSearch";
        var selectFields = string.Join(", ", searchOptions.Select);

        var facetsInfo = includeFacets ? "category, brand" : "無";

        if (queryVector != null)
        {
            _logger.LogInformation(
                "[Azure AI Search] 混合搜尋查詢 | QueryType={QueryType} | SearchText={SearchText} | Filter={Filter} | VectorSearch: KNN={KNN}, Field=combinedEmbedding, Dimensions={Dimensions} | Size={Size} | Select={Select} | Facets={Facets}",
                queryType,
                searchTextQuery,
                filterString,
                knnCount,
                queryVector.Length,
                searchOptions.Size,
                selectFields,
                facetsInfo
            );

            // 記錄可在 Azure Portal 使用的查詢格式
            _logger.LogInformation(
                "[Azure AI Search] Portal Query Format | SearchText: {SearchText} | Filter: {Filter} | VectorSearch: KNN={KNN}, Field=combinedEmbedding | Facets: {Facets}",
                searchTextQuery,
                filterString,
                knnCount,
                facetsInfo
            );
        }
        else
        {
            _logger.LogInformation(
                "[Azure AI Search] 文字搜尋查詢 | QueryType={QueryType} | SearchText={SearchText} | Filter={Filter} | Size={Size} | Select={Select} | Facets={Facets}",
                queryType,
                searchTextQuery,
                filterString,
                searchOptions.Size,
                selectFields,
                facetsInfo
            );

            // 記錄可在 Azure Portal 使用的查詢格式
            _logger.LogInformation(
                "[Azure AI Search] Portal Query Format | SearchText: {SearchText} | Filter: {Filter} | Facets: {Facets}",
                searchTextQuery,
                filterString,
                facetsInfo
            );
        }

        var response = await _searchClient.SearchAsync<ProductV2SearchDocument>(searchTextQuery, searchOptions);

        // 記錄結果（包含 facets 和 ResultsCount）
        var facetsCount = includeFacets && response.Value.Facets != null
            ? $"CategoryFacets={(response.Value.Facets.TryGetValue("category", out var categoryFacet) ? categoryFacet.Count : 0)}, BrandFacets={(response.Value.Facets.TryGetValue("brand", out var brandFacet) ? brandFacet.Count : 0)}"
            : "無 Facets";

        _logger.LogInformation(
            "[Azure AI Search] 搜尋結果 | TotalCount={TotalCount} | ResultsCount={ResultsCount} | {FacetsInfo}",
            response.Value.TotalCount ?? 0,
            response.Value.GetResults().Count(),
            facetsCount
        );

        return response.Value;
    }

    /// <summary>
    /// 取得所有分類（使用 Facets）
    /// </summary>
    public async Task<List<string>> GetCategoriesAsync()
    {
        var searchOptions = new SearchOptions
        {
            Size = 0,
            Facets = { "category" }
        };

        _logger.LogInformation(
            "[Azure AI Search] Facet 查詢 | QueryType=GetCategories | Facets={Facets}",
            string.Join(", ", searchOptions.Facets)
        );

        var response = await _searchClient.SearchAsync<ProductV2SearchDocument>("*", searchOptions);

        if (response.Value.Facets != null && response.Value.Facets.ContainsKey("category"))
        {
            var categories = response.Value.Facets["category"]
                .Select(f => f.Value.ToString() ?? string.Empty)
                .Where(c => !string.IsNullOrEmpty(c))
                .ToList();

            _logger.LogInformation(
                "[Azure AI Search] Facet 結果 | CategoriesCount={Count}",
                categories.Count
            );

            return categories;
        }

        return new List<string>();
    }

    /// <summary>
    /// 取得所有品牌（使用 Facets）
    /// </summary>
    public async Task<List<string>> GetBrandsAsync()
    {
        var searchOptions = new SearchOptions
        {
            Size = 0,
            Facets = { "brand" }
        };

        _logger.LogInformation(
            "[Azure AI Search] Facet 查詢 | QueryType=GetBrands | Facets={Facets}",
            string.Join(", ", searchOptions.Facets)
        );

        var response = await _searchClient.SearchAsync<ProductV2SearchDocument>("*", searchOptions);

        if (response.Value.Facets != null && response.Value.Facets.ContainsKey("brand"))
        {
            var brands = response.Value.Facets["brand"]
                .Select(f => f.Value.ToString() ?? string.Empty)
                .Where(b => !string.IsNullOrEmpty(b))
                .Distinct()
                .ToList();

            _logger.LogInformation(
                "[Azure AI Search] Facet 結果 | BrandsCount={Count}",
                brands.Count
            );

            return brands;
        }

        return new List<string>();
    }

    /// <summary>
    /// 檢查索引是否存在
    /// </summary>
    public async Task<bool> IndexExistsAsync(string indexName)
    {
        try
        {
            await _indexClient.GetIndexAsync(indexName);
            return true;
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            return false;
        }
    }

    /// <summary>
    /// 刪除索引
    /// </summary>
    public async Task DeleteIndexAsync(string indexName)
    {
        try
        {
            await _indexClient.DeleteIndexAsync(indexName);
            _logger.LogInformation("Index {IndexName} deleted successfully", indexName);
        }
        catch (RequestFailedException ex) when (ex.Status == 404)
        {
            _logger.LogInformation("Index {IndexName} does not exist, skipping deletion", indexName);
        }
    }

    /// <summary>
    /// 創建索引
    /// </summary>
    public async Task CreateIndexAsync(string indexName)
    {
        var fields = new List<SearchField>
        {
            new SimpleField("id", SearchFieldDataType.String) { IsKey = true },
            new SearchableField("name") 
            { 
                IsFilterable = false, 
                IsFacetable = false,
                AnalyzerName = LexicalAnalyzerName.ZhHantMicrosoft
            },
            new SearchField("nameEmbedding", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = 1536,
                VectorSearchProfileName = "default"
            },
            new SearchableField("description") 
            { 
                IsFilterable = false, 
                IsFacetable = false,
                AnalyzerName = LexicalAnalyzerName.ZhHantMicrosoft
            },
            new SearchField("descriptionEmbedding", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = 1536,
                VectorSearchProfileName = "default"
            },
            new SimpleField("price", SearchFieldDataType.Double) { IsFilterable = true, IsSortable = true },
            new SimpleField("category", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
            new SimpleField("subcategories", SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true },
            new SimpleField("brand", SearchFieldDataType.String) { IsFilterable = true, IsFacetable = true },
            new SimpleField("color", SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("size", SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("material", SearchFieldDataType.String) { IsFilterable = true },
            new SimpleField("image", SearchFieldDataType.String),
            new SimpleField("images", SearchFieldDataType.Collection(SearchFieldDataType.String)),
            new SimpleField("tags", SearchFieldDataType.Collection(SearchFieldDataType.String)) { IsFilterable = true },
            // attributes 序列化為 JSON 字串存儲（Azure AI Search 不直接支持 Dictionary）
            new SearchableField("attributes") 
            { 
                IsFilterable = false, 
                IsFacetable = false,
                AnalyzerName = LexicalAnalyzerName.ZhHantMicrosoft
            },
            new SearchField("reviewsEmbedding", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = 1536,
                VectorSearchProfileName = "default"
            },
            new SimpleField("createdAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
            new SimpleField("updatedAt", SearchFieldDataType.DateTimeOffset) { IsFilterable = true, IsSortable = true },
            new SearchField("combinedEmbedding", SearchFieldDataType.Collection(SearchFieldDataType.Single))
            {
                IsSearchable = true,
                VectorSearchDimensions = 1536,
                VectorSearchProfileName = "default"
            }
        };

        // 添加 reviews 複雜欄位（陣列）
        var reviewsField = new ComplexField("reviews", collection: true);
        reviewsField.Fields.Add(new SimpleField("rating", SearchFieldDataType.Int32) { IsFilterable = true });
        reviewsField.Fields.Add(new SearchableField("comment") 
        { 
            IsFilterable = false,
            AnalyzerName = LexicalAnalyzerName.ZhHantMicrosoft
        });
        fields.Add(reviewsField);

        // 創建 Semantic Configuration（用於 Semantic Ranker）
        var semanticConfig = new SemanticConfiguration("default", new SemanticPrioritizedFields
        {
            TitleField = new SemanticField("name"),
            ContentFields =
            {
                new SemanticField("description"),
                new SemanticField("attributes")
            },
            KeywordsFields =
            {
                new SemanticField("tags"),
                new SemanticField("category"),
                new SemanticField("brand")
            }
        });

        var index = new SearchIndex(indexName)
        {
            Fields = fields,
            VectorSearch = new VectorSearch
            {
                Profiles =
                {
                    new VectorSearchProfile("default", "default-hnsw")
                },
                Algorithms =
                {
                    new HnswAlgorithmConfiguration("default-hnsw")
                }
            },
            SemanticSearch = new SemanticSearch
            {
                Configurations = { semanticConfig }
            }
        };

        await _indexClient.CreateIndexAsync(index);
        _logger.LogInformation("Index {IndexName} created successfully", indexName);
    }

    /// <summary>
    /// 取得索引中的文檔數量
    /// </summary>
    public async Task<long> GetDocumentCountAsync()
    {
        var response = await _searchClient.GetDocumentCountAsync();
        return response.Value;
    }

    /// <summary>
    /// 上傳文檔到索引
    /// </summary>
    public async Task UploadDocumentsAsync<T>(IEnumerable<T> documents) where T : class
    {
        var documentsList = documents.ToList();
        var batch = IndexDocumentsBatch.Upload(documentsList);
        var response = await _searchClient.IndexDocumentsAsync(batch);

        if (response.Value.Results.Any(r => !r.Succeeded))
        {
            var errors = response.Value.Results.Where(r => !r.Succeeded).ToList();
            foreach (var error in errors)
            {
                _logger.LogError("Failed to index document {Key}: {ErrorMessage}", error.Key, error.ErrorMessage);
            }

            throw new InvalidOperationException($"Failed to index {errors.Count} documents");
        }

        _logger.LogInformation("Successfully uploaded {Count} documents", documentsList.Count);
    }

    /// <summary>
    /// 上傳 ProductV2 文檔到索引（自動轉換為 ProductV2SearchDocument）
    /// </summary>
    public async Task UploadProductV2DocumentsAsync(IEnumerable<ProductV2> products)
    {
        var searchDocuments = products.Select(ProductV2SearchDocument.FromProductV2).ToList();
        await UploadDocumentsAsync(searchDocuments);
    }
}