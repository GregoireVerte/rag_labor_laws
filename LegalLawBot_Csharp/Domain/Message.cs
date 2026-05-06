namespace LegalLawBot_Csharp.Domain;

public enum MessageRole
{
    User,
    Assistant
}

public class Message
{
    public Guid Id { get; private set; } = Guid.NewGuid();
    public MessageRole Role { get; private set; }
    public string Content { get; private set; }
    public DateTime CreatedAt { get; private set; } = DateTime.UtcNow;

    // Źródła – wypełniane tylko, gdy Role == Assistant
    public IReadOnlyList<ArticleId> Sources { get; private set; } = new List<ArticleId>();

    // Konstruktor dla EF Core
    private Message() { Content = null!; }

    public Message(MessageRole role, string content, IEnumerable<ArticleId>? sources = null)
    {
        if (string.IsNullOrWhiteSpace(content))
            throw new ArgumentException("Treść wiadomości nie może być pusta.");

        Role = role;
        Content = content;
        Sources = sources?.ToList().AsReadOnly() ?? new List<ArticleId>().AsReadOnly();
    }
}