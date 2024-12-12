using StrongBuy.Blazor.Components;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;
using StrongBuy.Blazor.Models;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddRazorComponents()
    .AddInteractiveServerComponents()
    ;

// Add DbContext
builder.Services.AddDbContext<StrongBuyContext>(options =>
    options.UseSqlite(builder.Configuration.GetConnectionString("DefaultConnection")));

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