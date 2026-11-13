using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Smiley;

public sealed class ChatProviderException(string message, bool connectionUnavailable = false) : Exception(message)
{
    public bool ConnectionUnavailable { get; } = connectionUnavailable;
}
public sealed record OllamaModel(string Name);
public sealed record OllamaModelList(IReadOnlyList<OllamaModel> Models, int RemoteModelsExcluded);

// Independent text-only provider. One shared client, loopback only, no redirects/proxy/auth or model downloads.
public sealed class OllamaChatProvider : IChatProvider
{
    private static readonly HttpClient Client = new(new SocketsHttpHandler { UseProxy = false, AllowAutoRedirect = false })
    { Timeout = Timeout.InfiniteTimeSpan };
    private readonly Uri server;
    public string Model { get; }
    public OllamaChatProvider(string model = "", Uri? server = null)
    {
        this.server = server ?? new Uri("http://localhost:11434/");
        if (!this.server.IsAbsoluteUri || this.server.Scheme != "http" || !this.server.IsLoopback ||
            this.server.UserInfo.Length != 0 || this.server.AbsolutePath != "/" || this.server.Query.Length != 0 || this.server.Fragment.Length != 0)
            throw new ArgumentException("Ollama deve usar um endereço HTTP local (loopback).", nameof(server));
        Model = model;
    }

    public async Task<OllamaModelList> GetModelsAsync(CancellationToken cancellationToken = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, new Uri(server, "api/tags"));
        using var document = await SendAsync(request, TimeSpan.FromSeconds(5), cancellationToken).ConfigureAwait(false);
        try
        {
            var response = document.RootElement.Deserialize<ModelsResponse>();
            if (response?.Models is null) throw new JsonException();
            var models = new List<OllamaModel>(); int excluded = 0;
            foreach (var item in response.Models)
            {
                string? name = item?.Name ?? item?.Model;
                if (string.IsNullOrWhiteSpace(name)) continue;
                if (item!.Size <= 0 || !string.IsNullOrEmpty(item.RemoteHost) || !string.IsNullOrEmpty(item.RemoteModel) ||
                    name.Contains("-cloud", StringComparison.OrdinalIgnoreCase) || name.EndsWith(":cloud", StringComparison.OrdinalIgnoreCase))
                { excluded++; continue; }
                if (!models.Any(m => m.Name == name)) models.Add(new(name));
            }
            models.Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a.Name, b.Name));
            return new(models.AsReadOnly(), excluded);
        }
        catch (JsonException) { throw new ChatProviderException("Ollama retornou uma lista de modelos inválida."); }
    }

    public async Task<string> GetReplyAsync(ChatRequest request, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(Model)) throw new ChatProviderException("Selecione um modelo local antes de enviar.");
        // Recheck local metadata at send time, including when Ollama changed since the picker was refreshed.
        var available = await GetModelsAsync(cancellationToken).ConfigureAwait(false);
        if (!available.Models.Any(m => m.Name == Model))
            throw new ChatProviderException("O modelo selecionado não está disponível como modelo local. Atualize a lista.");
        var messages = new List<object> { new { role = "system", content = request.SystemInstruction } };
        foreach (var message in request.Messages.TakeLast(8))
        {
            if (message.Role is not ("user" or "assistant")) throw new ChatProviderException("O contexto contém um papel de mensagem inválido.");
            messages.Add(new { role = message.Role, content = message.Content });
        }
        using var httpRequest = new HttpRequestMessage(HttpMethod.Post, new Uri(server, "api/chat"))
        {
            Content = JsonContent.Create(new { model = Model, messages, stream = false, options = new { num_predict = 512 } })
        };
        using var document = await SendAsync(httpRequest, TimeSpan.FromMinutes(3), cancellationToken).ConfigureAwait(false);
        try
        {
            var response = document.RootElement.Deserialize<ReplyResponse>();
            if (!string.IsNullOrEmpty(response?.RemoteHost) || !string.IsNullOrEmpty(response?.RemoteModel))
                throw new ChatProviderException("Ollama identificou processamento remoto. Esta versão aceita apenas modelos locais.");
            if (response is null || !response.Done || response.Message?.Role != "assistant" || string.IsNullOrWhiteSpace(response.Message.Content))
                throw new ChatProviderException("Ollama retornou uma resposta vazia ou inválida. Tente outro modelo local.");
            return response.Message.Content.Trim();
        }
        catch (JsonException) { throw new ChatProviderException("Ollama retornou uma resposta inválida."); }
    }

    private static async Task<JsonDocument> SendAsync(HttpRequestMessage request, TimeSpan timeout, CancellationToken cancellationToken)
    {
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        deadline.CancelAfter(timeout);
        try
        {
            using var response = await Client.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, deadline.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw new ChatProviderException(response.StatusCode == HttpStatusCode.NotFound
                    ? "Modelo ou endpoint não encontrado no Ollama. Atualize a lista e confira a instalação."
                    : $"Ollama recusou a solicitação (HTTP {(int)response.StatusCode}). Verifique o serviço e o modelo.");
            await using var stream = await response.Content.ReadAsStreamAsync(deadline.Token).ConfigureAwait(false);
            var document = await JsonDocument.ParseAsync(stream, cancellationToken: deadline.Token).ConfigureAwait(false);
            if (document.RootElement.ValueKind != JsonValueKind.Object || document.RootElement.TryGetProperty("error", out _))
            { document.Dispose(); throw new ChatProviderException("Ollama informou uma falha na solicitação. Confira o modelo selecionado."); }
            return document;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        { throw new ChatProviderException("Ollama excedeu o tempo de espera. O primeiro carregamento pode demorar; tente novamente."); }
        catch (HttpRequestException)
        { throw new ChatProviderException("Ollama indisponível em localhost:11434. Abra o Ollama e clique em Atualizar.", true); }
        catch (System.IO.IOException)
        { throw new ChatProviderException("A conexão com o Ollama foi interrompida. Atualize a lista e tente novamente.", true); }
        catch (JsonException) { throw new ChatProviderException("Ollama retornou dados inválidos em vez de uma resposta JSON."); }
    }

    private sealed class ModelsResponse { [JsonPropertyName("models")] public List<ModelInfo?>? Models { get; set; } }
    private sealed class ModelInfo
    {
        [JsonPropertyName("name")] public string? Name { get; set; }
        [JsonPropertyName("model")] public string? Model { get; set; }
        [JsonPropertyName("size")] public long Size { get; set; }
        [JsonPropertyName("remote_host")] public string? RemoteHost { get; set; }
        [JsonPropertyName("remote_model")] public string? RemoteModel { get; set; }
    }
    private sealed class ReplyResponse
    {
        [JsonPropertyName("done")] public bool Done { get; set; }
        [JsonPropertyName("message")] public ReplyMessage? Message { get; set; }
        [JsonPropertyName("remote_host")] public string? RemoteHost { get; set; }
        [JsonPropertyName("remote_model")] public string? RemoteModel { get; set; }
    }
    private sealed class ReplyMessage
    {
        [JsonPropertyName("role")] public string? Role { get; set; }
        [JsonPropertyName("content")] public string? Content { get; set; }
    }
}
