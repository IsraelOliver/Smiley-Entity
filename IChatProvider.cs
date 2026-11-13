namespace Smiley;

public sealed record ChatMessage(string Role, string Content);
public sealed record ChatRequest(string SystemInstruction, IReadOnlyList<ChatMessage> Messages, int MaximumOutputTokens = 512,
    bool RequestActions = false, string? ActionContext = null);

public interface IChatProvider
{
    Task<string> GetReplyAsync(ChatRequest request, CancellationToken cancellationToken = default);
}

// Demonstration only: no model, network, persistence or background observation.
public sealed class MockChatProvider : IChatProvider
{
    public async Task<string> GetReplyAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        await Task.Delay(300, cancellationToken);
        string message = request.Messages.LastOrDefault(m => m.Role == "user")?.Content ?? "";
        if (message.Contains("fazendo", StringComparison.OrdinalIgnoreCase))
            return "Avaliando as escolhas questionáveis que me trouxeram até este computador.";
        if (message.Contains("oi", StringComparison.OrdinalIgnoreCase) || message.Contains("olá", StringComparison.OrdinalIgnoreCase) || message.Contains("ola", StringComparison.OrdinalIgnoreCase))
            return "Olha só. Meu criador finalmente resolveu conversar comigo.";
        if (message.Contains("pular", StringComparison.OrdinalIgnoreCase))
            return "Eu pulo entre janelas. Você pula as atualizações. Cada um com seus talentos.";
        return "Estou ouvindo. Por enquanto, minhas respostas são ensaiadas. O sarcasmo já veio de fábrica.";
    }
}
