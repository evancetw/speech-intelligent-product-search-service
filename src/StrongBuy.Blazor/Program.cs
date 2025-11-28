using System.ClientModel;
using System.Security;
using StrongBuy.Blazor.Components;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using Azure;
using StrongBuy.Blazor.Models;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using StrongBuy.Blazor.Services;
// using OpenAI;
// using OpenAI.Embeddings;
using Azure.AI.OpenAI;
using OpenAI.Embeddings;
using System.Text;
using System.Net.Http.Headers;
using StrongBuy.Blazor.Extensions;
using StrongBuy.Core.Models;

namespace StrongBuy.Blazor;

public class Program
{
    public static async Task Main(string[] args)
    {
        var builder = WebApplication.CreateBuilder(args);

        // Add services to the container.
        builder.Services.AddRazorComponents()
            .AddInteractiveServerComponents();

        // Add DbContext
        builder.Services.AddDbContext<StrongBuyContext>(options =>
            options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

        // Add Elasticsearch
        builder.Services.AddElasticsearchService(builder.Configuration);

        // Add Azure AI Search
        builder.Services.AddAzureSearchService(builder.Configuration);

        // Add OpenAI
        builder.Services.AddOpenAi(builder.Configuration);

        // Add Persona Service
        builder.Services.AddScoped<PersonaService>();

        // Add Agent Service
        builder.Services.AddScoped<AgentService>();

        var app = builder.Build();

        // Initialize Database
        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            try
            {
                var context = services.GetRequiredService<StrongBuyContext>();

                // Ensure database is created and apply migrations
                await context.Database.MigrateAsync();

                // Check if database needs seeding
                if (!context.Products.Any())
                {
                    // Read products from JSON file
                    var jsonPath = Path.Combine(builder.Environment.ContentRootPath, "Data", "products.json");
                    var jsonString = await File.ReadAllTextAsync(jsonPath);
                    var products = JsonSerializer.Deserialize<List<Product>>(jsonString,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (products != null)
                    {
                        context.Products.AddRange(products);
                        await context.SaveChangesAsync();
                    }
                }
            }
            catch (Exception ex)
            {
                var logger = services.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "An error occurred while initializing the database.");
                return;
            }
        }

        // 確認是否能連線到 Elasticsearch 並初始化資料
        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            try
            {
                var client = services.GetRequiredService<ElasticsearchClient>();
                var logger = services.GetRequiredService<ILogger<Program>>();
                var configuration = services.GetRequiredService<IConfiguration>();

                // 1. 檢查連線
                var pingResponse = await client.PingAsync();
                if (!pingResponse.IsValidResponse)
                {
                    logger.LogError("Failed to connect to Elasticsearch");
                    return;
                }

                logger.LogInformation("Successfully connected to Elasticsearch");

                // 2. 檢查索引是否存在
                var indexName = "products";
                var indexExistsResponse = await client.Indices.ExistsAsync(indexName);

                // 3. 如果索引不存在，建立索引
                if (!indexExistsResponse.Exists)
                {
                    logger.LogInformation("Creating index: {IndexName}", indexName);

                    // 讀取 mapping 檔案
                    var mappingPath = Path.Combine(
                        builder.Environment.ContentRootPath, "Data", "products.standard.esmapping.01.json");
                    var mappingJson = await File.ReadAllTextAsync(mappingPath);

                    // 使用 HTTP 請求直接建立索引（因為我們有完整的 JSON 配置）
                    var esUri = configuration["Elasticsearch:Uri"] ?? "http://localhost:9200";
                    var username = configuration["Elasticsearch:Username"] ?? "elastic";
                    var password = configuration["Elasticsearch:Password"] ?? "espw";

                    using var httpClient = new HttpClient();
                    var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                    httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
                    httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                    var createIndexUrl = $"{esUri.TrimEnd('/')}/{indexName}";
                    var content = new StringContent(mappingJson, Encoding.UTF8, "application/json");
                    var httpResponse = await httpClient.PutAsync(createIndexUrl, content);

                    if (httpResponse.IsSuccessStatusCode)
                    {
                        logger.LogInformation("Index {IndexName} created successfully", indexName);
                    }
                    else
                    {
                        var errorContent = await httpResponse.Content.ReadAsStringAsync();
                        logger.LogError("Failed to create index {IndexName}: {StatusCode} - {Error}",
                            indexName, httpResponse.StatusCode, errorContent);
                        return;
                    }
                }
                // else
                // {
                //     // 4. 升級現有索引（檢查並更新 mapping）
                //     logger.LogInformation("Index {IndexName} already exists. Checking for updates...", indexName);
                //
                //     var mappingPath = Path.Combine(
                //         builder.Environment.ContentRootPath, "Data", "products.standard.esmapping.01.json");
                //     var mappingJson = await File.ReadAllTextAsync(mappingPath);
                //     var jsonDoc = JsonDocument.Parse(mappingJson);
                //
                //     // 使用 HTTP 請求更新 mapping（只能添加新欄位，不能修改現有欄位的類型）
                //     if (jsonDoc.RootElement.TryGetProperty("mappings", out var mappings))
                //     {
                //         try
                //         {
                //             var esUri = configuration["Elasticsearch:Uri"] ?? "http://localhost:9200";
                //             var username = configuration["Elasticsearch:Username"] ?? "elastic";
                //             var password = configuration["Elasticsearch:Password"] ?? "espw";
                //
                //             using var httpClient = new HttpClient();
                //             var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                //             httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
                //             httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                //
                //             // 只發送 mappings 部分
                //             var mappingsJson = JsonSerializer.Serialize(new { mappings = JsonSerializer.Deserialize<JsonElement>(mappings.GetRawText()) });
                //             var putMappingUrl = $"{esUri.TrimEnd('/')}/{indexName}/_mapping";
                //             var content = new StringContent(mappingsJson, Encoding.UTF8, "application/json");
                //             var httpResponse = await httpClient.PutAsync(putMappingUrl, content);
                //
                //             if (httpResponse.IsSuccessStatusCode)
                //             {
                //                 logger.LogInformation("Index mapping updated successfully");
                //             }
                //             else
                //             {
                //                 var errorContent = await httpResponse.Content.ReadAsStringAsync();
                //                 logger.LogWarning("Failed to update index mapping: {StatusCode} - {Error}",
                //                     httpResponse.StatusCode, errorContent);
                //             }
                //         }
                //         catch (Exception ex)
                //         {
                //             logger.LogWarning(ex, "Could not update index mapping. This is normal if mapping is already up to date.");
                //         }
                //     }
                //
                //     // 更新 settings（如果需要，注意：某些 settings 只能在建立索引時設定）
                //     if (jsonDoc.RootElement.TryGetProperty("settings", out var indexSettings))
                //     {
                //         try
                //         {
                //             var esUri = configuration["Elasticsearch:Uri"] ?? "http://localhost:9200";
                //             var username = configuration["Elasticsearch:Username"] ?? "elastic";
                //             var password = configuration["Elasticsearch:Password"] ?? "espw";
                //
                //             using var httpClient = new HttpClient();
                //             var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                //             httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
                //             httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                //
                //             // 只發送 settings 部分（排除 index 相關的設定）
                //             var settingsJson = JsonSerializer.Serialize(new { index = JsonSerializer.Deserialize<JsonElement>(indexSettings.GetRawText()) });
                //             var putSettingsUrl = $"{esUri.TrimEnd('/')}/{indexName}/_settings";
                //             var content = new StringContent(settingsJson, Encoding.UTF8, "application/json");
                //             var httpResponse = await httpClient.PutAsync(putSettingsUrl, content);
                //
                //             if (httpResponse.IsSuccessStatusCode)
                //             {
                //                 logger.LogInformation("Index settings updated successfully");
                //             }
                //             else
                //             {
                //                 var errorContent = await httpResponse.Content.ReadAsStringAsync();
                //                 logger.LogWarning("Could not update index settings: {StatusCode} - {Error}. Some settings can only be set at index creation.",
                //                     httpResponse.StatusCode, errorContent);
                //             }
                //         }
                //         catch (Exception ex)
                //         {
                //             logger.LogWarning(ex, "Could not update index settings: {Message}", ex.Message);
                //         }
                //     }
                // }

                // 5. 檢查索引中的文檔數量
                var countResponse = await client.CountAsync(c => c.Index(indexName));
                if (!countResponse.IsValidResponse)
                {
                    logger.LogError("Failed to get document count");
                    return;
                }

                // 6. 如果沒有文檔，則匯入資料
                if (countResponse.Count == 0)
                {
                    logger.LogInformation("No documents found in index. Starting import...");

                    // 讀取 products.json
                    var jsonPath = Path.Combine(builder.Environment.ContentRootPath, "Data", "products.json");
                    var jsonString = await File.ReadAllTextAsync(jsonPath);
                    var products = JsonSerializer.Deserialize<List<Product>>(jsonString,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (products == null || !products.Any())
                    {
                        logger.LogError("No products found in products.json");
                        return;
                    }

                    // 批量匯入資料
                    var bulkResponse = await client.BulkAsync(b => b
                        .Index(indexName)
                        .IndexMany(products)
                    );

                    if (!bulkResponse.IsValidResponse)
                    {
                        logger.LogError("Failed to import products: {Error}", bulkResponse.DebugInformation);
                        return;
                    }

                    logger.LogInformation("Successfully imported {Count} products", products.Count);
                }
                else
                {
                    logger.LogInformation("Index already contains {Count} documents", countResponse.Count);
                }
            }
            catch (Exception ex)
            {
                var logger = services.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "An error occurred while initializing Elasticsearch");
            }
        }

        // 確認是否能連線到 Elasticsearch 並初始化 products-v2 資料
        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;

            try
            {
                var client = services.GetRequiredService<ElasticsearchClient>();
                var loggerV2 = services.GetRequiredService<ILogger<Program>>();
                var configuration = services.GetRequiredService<IConfiguration>();

                // 1. 檢查連線
                var pingResponse = await client.PingAsync();
                if (!pingResponse.IsValidResponse)
                {
                    loggerV2.LogError("Failed to connect to Elasticsearch");
                    return;
                }

                loggerV2.LogInformation("Successfully connected to Elasticsearch");

                // 2. 檢查索引是否存在
                var indexName = "products-v2";
                var indexExistsResponse = await client.Indices.ExistsAsync(indexName);

                // 3. 如果索引不存在，建立索引
                if (!indexExistsResponse.Exists)
                {
                    loggerV2.LogInformation("Creating index: {IndexName}", indexName);

                    // 讀取 mapping 檔案
                    var mappingPath = Path.Combine(
                        builder.Environment.ContentRootPath, "Data", "products.standard.esmapping.02.json");

                    if (!File.Exists(mappingPath))
                    {
                        loggerV2.LogWarning("Mapping file not found: {MappingPath}. Skipping index creation.", mappingPath);
                    }
                    else
                    {
                        var mappingJson = await File.ReadAllTextAsync(mappingPath);

                        // 使用 HTTP 請求直接建立索引（因為我們有完整的 JSON 配置）
                        var esUri = configuration["Elasticsearch:Uri"] ?? "http://localhost:9200";
                        var username = configuration["Elasticsearch:Username"] ?? "elastic";
                        var password = configuration["Elasticsearch:Password"] ?? "espw";

                        using var httpClient = new HttpClient();
                        var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                        httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
                        httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

                        var createIndexUrl = $"{esUri.TrimEnd('/')}/{indexName}";
                        var content = new StringContent(mappingJson, Encoding.UTF8, "application/json");
                        var httpResponse = await httpClient.PutAsync(createIndexUrl, content);

                        if (httpResponse.IsSuccessStatusCode)
                        {
                            loggerV2.LogInformation("Index {IndexName} created successfully", indexName);
                        }
                        else
                        {
                            var errorContent = await httpResponse.Content.ReadAsStringAsync();
                            loggerV2.LogError("Failed to create index {IndexName}: {StatusCode} - {Error}",
                                indexName, httpResponse.StatusCode, errorContent);
                            return;
                        }
                    }
                }
                // else
                // {
                //     // 4. 升級現有索引（檢查並更新 mapping）
                //     loggerV2.LogInformation("Index {IndexName} already exists. Checking for updates...", indexName);
                //
                //     var mappingPath = Path.Combine(
                //         builder.Environment.ContentRootPath, "Data", "products-v2.smartcn.esmapping.json");
                //
                //     if (File.Exists(mappingPath))
                //     {
                //         var mappingJson = await File.ReadAllTextAsync(mappingPath);
                //         var jsonDoc = JsonDocument.Parse(mappingJson);
                //
                //         // 使用 HTTP 請求更新 mapping（只能添加新欄位，不能修改現有欄位的類型）
                //         if (jsonDoc.RootElement.TryGetProperty("mappings", out var mappings))
                //         {
                //             try
                //             {
                //                 var esUri = configuration["Elasticsearch:Uri"] ?? "http://localhost:9200";
                //                 var username = configuration["Elasticsearch:Username"] ?? "elastic";
                //                 var password = configuration["Elasticsearch:Password"] ?? "espw";
                //
                //                 using var httpClient = new HttpClient();
                //                 var authValue = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{username}:{password}"));
                //                 httpClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authValue);
                //                 httpClient.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
                //
                //                 // 只發送 mappings 部分
                //                 var mappingsJson = JsonSerializer.Serialize(new { mappings = JsonSerializer.Deserialize<JsonElement>(mappings.GetRawText()) });
                //                 var putMappingUrl = $"{esUri.TrimEnd('/')}/{indexName}/_mapping";
                //                 var content = new StringContent(mappingsJson, Encoding.UTF8, "application/json");
                //                 var httpResponse = await httpClient.PutAsync(putMappingUrl, content);
                //
                //                 if (httpResponse.IsSuccessStatusCode)
                //                 {
                //                     loggerV2.LogInformation("Index mapping updated successfully");
                //                 }
                //                 else
                //                 {
                //                     var errorContent = await httpResponse.Content.ReadAsStringAsync();
                //                     loggerV2.LogWarning("Failed to update index mapping: {StatusCode} - {Error}",
                //                         httpResponse.StatusCode, errorContent);
                //                 }
                //             }
                //             catch (Exception ex)
                //             {
                //                 loggerV2.LogWarning(ex, "Could not update index mapping. This is normal if mapping is already up to date.");
                //             }
                //         }
                //     }
                // }

                // 5. 檢查索引中的文檔數量
                var countResponse = await client.CountAsync(c => c.Index(indexName));
                if (!countResponse.IsValidResponse)
                {
                    loggerV2.LogError("Failed to get document count");
                    return;
                }

                // 6. 如果沒有文檔，則匯入資料
                if (countResponse.Count == 0)
                {
                    loggerV2.LogInformation("No documents found in index. Starting import...");

                    // // 初始化 OpenAI 客戶端
                    // var endpoint =
                    //     new Uri(
                    //         "https://openai-dotnetconf.openai.azure.com/openai/deployments/text-embedding-3-small/embeddings?api-version=2023-05-15"); // 2023-05-15
                    // // var endpoint =
                    // //     new Uri(
                    // //         "https://openai-dotnetconf.openai.azure.com"); // 2023-05-15
                    // var credential = configuration["OpenAI:ApiKey"];
                    // var apiKeyCredential = new ApiKeyCredential(credential);
                    // var model = "text-embedding-3-small";
                    //
                    // var openAIOptions = new OpenAIClientOptions()
                    // {
                    //     Endpoint = endpoint
                    // };
                    //
                    // var embeddingClient = new EmbeddingClient(model, apiKeyCredential, openAIOptions);


                    // var qq =  embeddingClient.GenerateEmbeddings(new List<string> { "first phrase", "second phrase", "third phrase" },
                    //      options: new EmbeddingGenerationOptions());


                    // var openAiKey = configuration["OpenAI:ApiKey"];
                    // var openAiClient = new OpenAIClient(new AzureKeyCredential(openAiKey));
                    // var embeddingClient = openAiClient.GetEmbeddingClient("text-embedding-3-small");

                    // 讀取 products.json
                    var jsonPath = Path.Combine(builder.Environment.ContentRootPath, "Data", "products.json");
                    var jsonString = await File.ReadAllTextAsync(jsonPath);
                    var products = JsonSerializer.Deserialize<List<ProductV2>>(jsonString,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (products == null || !products.Any())
                    {
                        loggerV2.LogError("No products found in products.json");
                        return;
                    }

                    var embeddingClient = services.GetRequiredService<EmbeddingClient>();

                    // 批次處理產品
                    var batchSize = 10;
                    for (var i = 0; i < products.Count; i += batchSize)
                    {
                        var batch = products.Skip(i).Take(batchSize);

                        foreach (var product in batch)
                        {
                            try
                            {
                                // 生成 embeddings
                                var texts = new List<string>()
                                {
                                    product.Name,
                                    product.Description,
                                    string.Join("\\n", product.Reviews.Select(x => x.Comment)),
                                };
                                texts.Add(texts[0] + "\\n" + texts[1] + "\\n" + texts[2]);

                                var embeddingsResult = await embeddingClient.GenerateEmbeddingsAsync(texts);
                                var nameEmbedding = embeddingsResult.Value[0].ToFloats();
                                var descriptionEmbedding = embeddingsResult.Value[1].ToFloats();
                                var reviewsEmbedding = embeddingsResult.Value[2].ToFloats();
                                var combinedEmbedding = embeddingsResult.Value[3].ToFloats();

                                product.NameEmbedding = nameEmbedding;
                                product.DescriptionEmbedding = descriptionEmbedding;
                                product.ReviewsEmbedding = reviewsEmbedding;
                                product.CombinedEmbedding = combinedEmbedding;
                            }
                            catch (Exception e)
                            {
                                Console.WriteLine(e);
                                throw;
                            }
                        }

                        // 批量匯入資料
                        var bulkResponse = await client.BulkAsync(b => b
                            .Index(indexName)
                            .IndexMany(batch)
                        );


                        loggerV2.LogInformation("Successfully imported all products with embeddings");
                    }

                    // else
                    // {
                    //     loggerV2.LogInformation("Index already contains {Count} documents", countResponse.Count);
                    // }
                }
            }
            catch (Exception ex)
            {
                var logger = services.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "An error occurred while initializing Elasticsearch");
            }
        }

        // 確認是否能連線到 Azure AI Search 並初始化資料
        using (var scope = app.Services.CreateScope())
        {
            var services = scope.ServiceProvider;
            try
            {
                var azureSearchService = services.GetRequiredService<AzureSearchService>();
                var loggerAzure = services.GetRequiredService<ILogger<Program>>();
                var configuration = services.GetRequiredService<IConfiguration>();
                var embeddingClient = services.GetRequiredService<EmbeddingClient>();

                var indexName = configuration["AzureSearch:IndexName"] ?? "products-v2";

                // 1. 檢查索引是否存在
                var indexExists = await azureSearchService.IndexExistsAsync(indexName);

                if (!indexExists)
                {
                    // 索引不存在，創建索引
                    loggerAzure.LogInformation("Index {IndexName} does not exist. Creating index...", indexName);
                    await azureSearchService.CreateIndexAsync(indexName);
                }
                else
                {
                    loggerAzure.LogInformation("Index {IndexName} already exists", indexName);
                }

                // 2. 檢查索引中的文檔數量
                var documentCount = await azureSearchService.GetDocumentCountAsync();
                loggerAzure.LogInformation("Index {IndexName} contains {Count} documents", indexName, documentCount);

                // 3. 如果沒有文檔，則匯入資料
                if (documentCount == 0)
                {
                    loggerAzure.LogInformation("No documents found in index. Starting import...");

                    // 讀取 products.json
                    var jsonPath = Path.Combine(builder.Environment.ContentRootPath, "Data", "products.json");
                    var jsonString = await File.ReadAllTextAsync(jsonPath);
                    var products = JsonSerializer.Deserialize<List<ProductV2>>(jsonString,
                        new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                    if (products == null || !products.Any())
                    {
                        loggerAzure.LogError("No products found in products.json");
                        return;
                    }

                    // 批次處理產品
                    var batchSize = 10;
                    for (var i = 0; i < products.Count; i += batchSize)
                    {
                        var batch = products.Skip(i).Take(batchSize).ToList();

                        foreach (var product in batch)
                        {
                            try
                            {
                                // 生成 embeddings（如果還沒有）
                                if (product.CombinedEmbedding == null || product.CombinedEmbedding.Value.IsEmpty)
                                {
                                    var texts = new List<string>()
                                    {
                                        product.Name,
                                        product.Description,
                                        string.Join("\\n", product.Reviews.Select(x => x.Comment)),
                                    };
                                    texts.Add(texts[0] + "\\n" + texts[1] + "\\n" + texts[2]);

                                    var embeddingsResult = await embeddingClient.GenerateEmbeddingsAsync(texts);
                                    var nameEmbedding = embeddingsResult.Value[0].ToFloats();
                                    var descriptionEmbedding = embeddingsResult.Value[1].ToFloats();
                                    var reviewsEmbedding = embeddingsResult.Value[2].ToFloats();
                                    var combinedEmbedding = embeddingsResult.Value[3].ToFloats();

                                    product.NameEmbedding = nameEmbedding;
                                    product.DescriptionEmbedding = descriptionEmbedding;
                                    product.ReviewsEmbedding = reviewsEmbedding;
                                    product.CombinedEmbedding = combinedEmbedding;
                                }
                            }
                            catch (Exception e)
                            {
                                loggerAzure.LogError(e, "Failed to generate embeddings for product {ProductId}", product.Id);
                                throw;
                            }
                        }

                        // 批量匯入資料到 Azure AI Search
                        await azureSearchService.UploadProductV2DocumentsAsync(batch);
                        loggerAzure.LogInformation("Imported batch {BatchNumber} ({Start}-{End} of {Total})",
                            i / batchSize + 1, i + 1, Math.Min(i + batchSize, products.Count), products.Count);
                    }

                    loggerAzure.LogInformation("Successfully imported {Count} products to Azure AI Search", products.Count);
                }
                else
                {
                    loggerAzure.LogInformation("Index already contains {Count} documents. Skipping import.", documentCount);
                }
            }
            catch (Exception ex)
            {
                var logger = services.GetRequiredService<ILogger<Program>>();
                logger.LogError(ex, "An error occurred while initializing Azure AI Search");
                // 不中斷應用程式啟動，只記錄錯誤
            }
        }

        // Configure the HTTP request pipeline.
        if (!app.Environment.IsDevelopment())
        {
            app.UseExceptionHandler("/Error", createScopeForErrors: true);
            app.UseHsts();
            app.UseHttpsRedirection();
        }

        // app.UseHttpsRedirection();

        app.UseStaticFiles();
        app.UseAntiforgery();

        app.MapRazorComponents<App>()
            .AddInteractiveServerRenderMode();

        await app.RunAsync();
    }
}