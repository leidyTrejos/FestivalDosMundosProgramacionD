namespace Itm.Search.Api.Models;

public class SearchEvent
{
    public int EventId { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string City { get; set; } = string.Empty;
    public string Genre { get; set; } = string.Empty;
}
