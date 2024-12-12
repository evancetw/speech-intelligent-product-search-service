using Elastic.Clients.Elasticsearch;
using Elastic.Clients.Elasticsearch.QueryDsl;
using System.Threading.Tasks;
using StrongBuy.Blazor.Models;

namespace StrongBuy.Blazor.Services;

public class ElasticsearchService
{
    private readonly ElasticsearchClient _client;

    public ElasticsearchService(ElasticsearchClient client)
    {
        _client = client;
    }

    public async Task<List<Product>> SearchBooksAsync(string searchTerm)
    {
        var response = await _client.SearchAsync<Product>(s => s
            .Query(q => q
                .Match(m => m
                    .Field(f => f.Name)
                    .Field(f => f.Description)
                    .Query(searchTerm)
                )
            )
        );

        return response.Documents.ToList();
    }
}