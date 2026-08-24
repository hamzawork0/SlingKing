namespace SlingKing.Domain.Platform;

public class AppSetting
{
    public string Key { get; set; } = null!;
    public string Value { get; set; } = null!;
    public DateTimeOffset UpdatedAt { get; set; }
}
