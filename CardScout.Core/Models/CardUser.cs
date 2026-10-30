namespace CardScout.Core.Models;

public class CardUser
{
    public long Id { get; set; }
    public string? Email { get; set; }
    public string? DisplayName { get; set; }
    public string IsActive { get; set; } = "Y";
    public DateTime CreatedAt { get; set; }
}
