using Azure;
using Azure.AI.OpenAI;
using Azure.Search.Documents;
using Azure.Search.Documents.Indexes;
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

    public static IServiceCollection AddAzureSearchService(this IServiceCollection services, IConfiguration configuration)
    {
        var searchServiceEndpoint = configuration["AzureSearch:Endpoint"];
        var searchServiceApiKey = configuration["AzureSearch:ApiKey"];
        var indexName = configuration["AzureSearch:IndexName"] ?? "products-v2";

        if (string.IsNullOrEmpty(searchServiceEndpoint))
        {
            throw new InvalidOperationException("AzureSearch:Endpoint is not configured");
        }

        if (string.IsNullOrEmpty(searchServiceApiKey))
        {
            throw new InvalidOperationException("AzureSearch:ApiKey is not configured");
        }

        services.AddScoped(provider =>
        {
            var endpoint = new Uri(searchServiceEndpoint);
            var credential = new AzureKeyCredential(searchServiceApiKey);
            var searchClient = new SearchClient(endpoint, indexName, credential);
            return searchClient;
        });

        services.AddScoped(provider =>
        {
            var endpoint = new Uri(searchServiceEndpoint);
            var credential = new AzureKeyCredential(searchServiceApiKey);
            var indexClient = new SearchIndexClient(endpoint, credential);
            return indexClient;
        });

        services.AddScoped<AzureSearchService>();

        return services;
    }
}