namespace SlingKing.Domain.Operations;

public class ExpenseCategory
{
    public int Id { get; set; }
    public string Name { get; set; } = null!;
    public bool IsActive { get; set; } = true;
}
