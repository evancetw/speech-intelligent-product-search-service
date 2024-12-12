namespace StrongBuy.Blazor.Services;

public class SharedService
{
    public SearchInputModel SearchInput { get; set; } = new();

    public class SearchInputModel
    {
        public string? SelectedCategory { get; set; } = string.Empty;
        public string? SearchText { get; set; } = string.Empty;
    }
}