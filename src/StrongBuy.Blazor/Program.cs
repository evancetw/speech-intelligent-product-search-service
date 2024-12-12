using StrongBuy.Blazor.Components;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using StrongBuy.Blazor.Models;
using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;

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
    // var settings = new ElasticsearchClientSettings(new Uri("http://localhost:9200"))
    //     .DefaultIndex("your_index_name"); // 替換為您的索引名稱
    // return new ElasticsearchClient(settings);

    return new ElasticsearchClient(
        "CloudId",
        new ApiKey("APIKey"));
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

// 確認是否能連線到 Elasticsearch 
using (var scope = app.Services.CreateScope())
{
    var services = scope.ServiceProvider;
    try
    {
        var client = services.GetRequiredService<ElasticsearchClient>();
        var response = await client.PingAsync();
        if (response.IsValidResponse)
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogInformation("Successfully connected to Elasticsearch");
        }
        else
        {
            var logger = services.GetRequiredService<ILogger<Program>>();
            logger.LogError("Failed to connect to Elasticsearch");
        }
    }
    catch (Exception ex)
    {
        var logger = services.GetRequiredService<ILogger<Program>>();
        logger.LogError(ex, "An error occurred while connecting to Elasticsearch");
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