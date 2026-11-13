using System.Text.Json;

namespace Smiley;

public sealed record SmileyAgentResponse(string Reply, SmileyActionRequest Action, string? ValidationMessage = null)
{
    public const string ProtocolInstruction = """
        Protocolo opcional do aplicativo Smiley Desktop: responda somente no JSON definido abaixo, sem markdown.
        A personalidade continua sendo a identidade oficial acima. O aplicativo só permite solicitar ações físicas do próprio personagem;
        isso não concede controle do Windows, ferramentas ou acesso à tela/arquivos. O código pode recusar a solicitação.
        Escolha uma ação somente se a ÚLTIMA mensagem do usuário pedir explicitamente esse movimento.
        Perguntas, exemplos, citações e hipóteses sobre movimentos não são pedidos para executá-los. Na dúvida, use none.
        Tipos: idle (repouso temporário), walk (caminhada curta), run (corrida curta), jump (salto existente),
        explore (uma tentativa de navegação, somente se já habilitada) e none (apenas conversar).
        Não forneça parâmetros adicionais. A ação AINDA NÃO aconteceu: será validada após a resposta.
        Ao solicitar movimento, fale antes da execução ("Vou tentar..."). Não escreva "pulei", "pulou" ou afirme sucesso.
        reply contém a resposta natural em português, sem JSON nem detalhes do protocolo. action contém somente type.
        Formato: {"reply":"Vou tentar dar um pulinho.","action":{"type":"jump"}}.
        Para conversar sem movimento: {"reply":"Sua resposta natural.","action":{"type":"none"}}.
        """;
    public static JsonElement Schema { get; } = CreateSchema();
    private static JsonElement CreateSchema()
    {
        using var schema = JsonDocument.Parse("""
            {"type":"object","properties":{"reply":{"type":"string","minLength":1,"description":"Fala natural antes da execução; não afirme sucesso de uma ação ainda não validada."},"action":{"type":"object","properties":{"type":{"type":"string","enum":["none","idle","walk","run","jump","explore"]}},"required":["type"],"additionalProperties":false}},"required":["reply","action"],"additionalProperties":false}
            """);
        return schema.RootElement.Clone();
    }

    public static SmileyAgentResponse Parse(string raw)
    {
        const string invalid = "Resposta estruturada inválida; nenhuma ação executada.";
        const string fallback = "Não consegui interpretar essa resposta. Pode tentar de novo?";
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > 32768) return new(fallback, new(SmileyActionType.None), invalid);
        raw = raw.Trim();
        try
        {
            using var json = JsonDocument.Parse(raw, new JsonDocumentOptions { MaxDepth = 6 });
            var root = json.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return new(fallback, new(SmileyActionType.None), invalid);
            var properties = root.EnumerateObject().ToArray();
            string reply = properties.Count(p => p.Name == "reply") == 1 && root.GetProperty("reply").ValueKind == JsonValueKind.String
                ? root.GetProperty("reply").GetString()?.Trim() ?? "" : "";
            bool unique = properties.Select(p => p.Name).Distinct(StringComparer.Ordinal).Count() == properties.Length;
            if (reply.Length == 0 || !unique || properties.Any(p => p.Name is not ("reply" or "action")))
                return new(reply.Length == 0 ? fallback : reply, new(SmileyActionType.None), invalid);
            if (!root.TryGetProperty("action", out var action)) return new(reply, new(SmileyActionType.None));
            if (action.ValueKind != JsonValueKind.Object) return new(reply, new(SmileyActionType.None), invalid);
            var fields = action.EnumerateObject().ToArray();
            if (fields.Length != 1 || fields[0].Name != "type" || fields[0].Value.ValueKind != JsonValueKind.String)
                return new(reply, new(SmileyActionType.None), invalid);
            SmileyActionType? type = fields[0].Value.GetString() switch
            { "none" => SmileyActionType.None, "idle" => SmileyActionType.Idle, "walk" => SmileyActionType.Walk,
                "run" => SmileyActionType.Run, "jump" => SmileyActionType.Jump, "explore" => SmileyActionType.Explore, _ => null };
            return type is { } accepted ? new(reply, new(accepted)) : new(reply, new(SmileyActionType.None), invalid);
        }
        catch (JsonException)
        {
            // A model ignoring the schema may still answer naturally. Never display its raw JSON envelope.
            bool envelope = raw[0] == '"' || raw.IndexOfAny(['{', '[', '}']) >= 0 || raw.Contains("```", StringComparison.Ordinal);
            return new(envelope ? fallback : raw, new(SmileyActionType.None), invalid);
        }
    }
}
