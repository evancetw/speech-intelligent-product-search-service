using StrongBuy.Blazor.Components;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using StrongBuy.Blazor.Models;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using StrongBuy.Blazor.Services;

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