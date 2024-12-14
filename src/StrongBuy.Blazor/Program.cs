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
using Azure;
using Azure.AI.OpenAI;
using OpenAI.Embeddings;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    ;

// Add DbContext
builder.Services.AddDbContext<StrongBuyContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

// 配置 Elasticsearch 客戶端
builder.Services.AddScoped(provider =>
{
    var settings = new ElasticsearchClientSettings(
            cloudId:
            "cloudid",
            credentials: new ApiKey("apikey")
        )
        .DefaultIndex("products");

    return new ElasticsearchClient(settings);
});

builder.Services.AddScoped<ElasticsearchService>();

// 配置
builder.Services.AddScoped(provider =>
{
    var configuration = provider.GetRequiredService<IConfiguration>();
    Uri oaiEndpoint = new("https://openai-dotnetconf.openai.azure.com");
    string oaiKey = configuration["OpenAI:ApiKey"];

    AzureKeyCredential credentials = new(oaiKey);

    Azure.AI.OpenAI.AzureOpenAIClient openAIClient = new(oaiEndpoint, credentials);
    return openAIClient;
});

builder.Services.AddScoped(provider =>
{
    var openAIClient = provider.GetRequiredService<AzureOpenAIClient>();
    var embeddingClient = openAIClient.GetEmbeddingClient("text-embedding-3-small");
    return embeddingClient;
});


var app = builder.Build();

// Initialize Database
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var context = services.GetRequiredService<StrongBuyContext>();

        // Ensure database is created and apply migrations
        context.Database.Migrate();

        // Check if database needs seeding
        if (!context.Products.Any())
        {
            // Read products from JSON file
            var jsonPath = Path.Combine(builder.Environment.ContentRootPath, "Data", "products.json");
            var jsonString = File.ReadAllText(jsonPath);
            var products = JsonSerializer.Deserialize<List<Product>>(jsonString,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

            if (products != null)
            {
                context.Products.AddRange(products);
                context.SaveChanges();
            }
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while initializing the database.");
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

        // 跳過這段，先手動建立索引
        // // 3. 如果索引不存在，建立索引
        // if (!indexExistsResponse.Exists)
        // {
        //     logger.LogInformation("Creating index: {IndexName}", indexName);
        //
        //     // 讀取 mapping 檔案
        //     var mappingPath = Path.Combine(
        //         builder.Environment.ContentRootPath, "Data", "products.smartcn.esmapping.json");
        //     var mappingJson = await File.ReadAllTextAsync(mappingPath);
        //
        //     var createIndexResponse = await client.Indices.CreateAsync(indexName, c => c
        //         .InitializeUsingJson(mappingJson)
        //     );
        //
        //     if (!createIndexResponse.IsValidResponse)
        //     {
        //         logger.LogError("Failed to create index: {Error}", createIndexResponse.DebugInformation);
        //         return;
        //     }
        //
        //     logger.LogInformation("Index created successfully");
        // }

        // 4. 檢查索引中的文檔數量
        var countResponse = await client.CountAsync(c => c.Index(indexName));
        if (!countResponse.IsValidResponse)
        {
            logger.LogError("Failed to get document count");
            return;
        }

        // 5. 如果沒有文檔，則匯入資料
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

        // 4. 檢查索引中的文檔數量
        var countResponse = await client.CountAsync(c => c.Index(indexName));
        if (!countResponse.IsValidResponse)
        {
            loggerV2.LogError("Failed to get document count");
            return;
        }

        // 5. 如果沒有文檔，則匯入資料
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

app.Run();