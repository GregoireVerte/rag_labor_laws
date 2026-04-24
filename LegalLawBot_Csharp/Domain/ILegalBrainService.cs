namespace LegalLawBot_Csharp.Domain;

public interface ILegalBrainService
{
    // Kontrakt na zadanie pytania do RAGa w Pythonie
    // Zwraca treść odpowiedzi i listę artykułów (źródeł)
    Task<(string Response, IEnumerable<ArticleId> Sources)> AskLegalQuestionAsync(UserQuery query);
}