using Azure;
using Azure.AI.OpenAI;
using Elastic.Clients.Elasticsearch;
using Elastic.Transport;
using StrongBuy.Blazor.Services;

namespace StrongBuy.Blazor.Extensions;

public static class ServiceCollectionExtensions
{
    public static IServiceCollection AddStrongBuy(this IServiceCollection services, IConfiguration configuration)
    {
        return services;
    }

    public static IServiceCollection AddElasticsearchService(this IServiceCollection services, IConfiguration configuration)
    {
        // 配置 Elasticsearch 客戶端
        services.AddScoped(provider =>
        {
            ElasticsearchClientSettings settings;
            var isLocal = true;
            if (isLocal)
            {
                var esUri = configuration["Elasticsearch:Uri"] ?? "http://localhost:9200";
                var username = configuration["Elasticsearch:Username"] ?? "elastic";
                var password = configuration["Elasticsearch:Password"] ?? "espw";
                settings = new ElasticsearchClientSettings(
                        new Uri(esUri))
                    .Authentication(new BasicAuthentication(username, password));
            }
            else
            {
                settings = new ElasticsearchClientSettings(
                    cloudId: configuration["Elasticsearch:Cloud:Id"] ?? "your_cloud_id",
                    credentials: new ApiKey(configuration["Elasticsearch:Cloud:ApiKey"] ?? "your_api_key")
                );
            }

            return new ElasticsearchClient(settings);
        });

        services.AddScoped<ElasticsearchService>();

        return services;
    }

    public static IServiceCollection AddOpenAi(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddScoped(provider =>
        {
            Uri oaiEndpoint = new("https://openai-dotnetconf.openai.azure.com");
            var oaiKey = configuration["OpenAI:ApiKey"];

            if (string.IsNullOrEmpty(oaiKey))
            {
                throw new InvalidOperationException("OpenAI:ApiKey is not configured");
            }

            AzureKeyCredential credentials = new(oaiKey);
            AzureOpenAIClient openAiClient = new(oaiEndpoint, credentials);
            return openAiClient;
        });

        services.AddScoped(provider =>
        {
            var openAiClient = provider.GetRequiredService<AzureOpenAIClient>();
            var embeddingClient = openAiClient.GetEmbeddingClient("text-embedding-3-small");
            return embeddingClient;
        });

        return services;
    }
}