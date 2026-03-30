public class ReviewSuggestionRequest
{
    public int SuggestionId { get; set; }
    public int DocumentPageId { get; set; }
    public string Action { get; set; } = "";
    public int ReviewedBy { get; set; }
    public int RoleId { get; set; }
}