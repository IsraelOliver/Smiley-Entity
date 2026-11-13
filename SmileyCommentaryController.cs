using System.Globalization;
using System.Text.RegularExpressions;

namespace Smiley;

public enum CommentaryKind { Click, DragStarted, Thrown, Landed, Jumped, SupportLost, CursorNear, WindowChanged, ForegroundChurn, Idle }
public readonly record struct CommentaryEvent(CommentaryKind Kind, nint Handle = 0, WindowChangeKind Changes = WindowChangeKind.None, bool Related = false);
public readonly record struct CommentarySpeech(string Text, bool FromOllama);
public readonly record struct ActivitySample(double IdleSeconds, nint Foreground, bool Interactive = true);
public sealed record CommentaryOptions(double GlobalCooldown = 25, double ImportantCooldown = 8,
    double AiCooldown = 90, double IdleThreshold = 240, double EventLifetime = 15, double AiTimeout = 12,
    double IdleAiTimeout = 60, double IdleEventLifetime = 75);

// Owned by the UI dispatcher. Observation never calls HTTP; only the independent timer calls PulseAsync.
public sealed class SmileyCommentaryController : IDisposable
{
    private readonly Func<double> now;
    private readonly Func<ChatSettings> settings;
    private readonly Func<ChatSettings, IChatProvider?> provider;
    private readonly Action<CommentarySpeech> speak;
    private readonly CommentaryOptions options;
    private readonly Dictionary<CommentaryKind, double> lastKind = new();
    private readonly Dictionary<CommentaryKind, int> variants = new();
    private readonly Queue<double> focusChanges = new();
    private (CommentaryEvent Event, double At)? pending;
    private CancellationTokenSource? generation;
    private CommentaryEvent generatingEvent;
    private long epoch;
    private double lastSpeech = double.NegativeInfinity, lastAi = double.NegativeInfinity, quietUntil;
    private int lastPriority;
    private bool enabled = true, paused, chatActive, disposed, idleSpoken;
    private double? sampleAt, idleBaseline;
    private double previousIdle;
    private nint foreground;
    public bool Enabled => enabled;
    public bool CanObserve => enabled && !paused && !chatActive && !disposed && now() >= quietUntil;
    public CommentarySpeech? LastSpeech { get; private set; }

    public SmileyCommentaryController(Func<double> now, Func<ChatSettings> settings,
        Func<ChatSettings, IChatProvider?> provider, Action<CommentarySpeech> speak, CommentaryOptions? options = null)
    { this.now = now; this.settings = settings; this.provider = provider; this.speak = speak; this.options = options ?? new(); }

    public void SetEnabled(bool value) { enabled = value; ResetObservation(); }
    public void SetPaused(bool value) { paused = value; ResetObservation(); }
    public void SetChatActive(bool value)
    {
        chatActive = value;
        if (!value) quietUntil = now() + 20; // Protect the last manual overlay, including after closing chat.
        ResetObservation();
    }
    public void ResetObservation()
    { CancelPending(); sampleAt = idleBaseline = null; idleSpoken = false; focusChanges.Clear(); foreground = 0; }
    private void CancelPending() { epoch++; pending = null; generation?.Cancel(); }
    private static int Priority(CommentaryEvent e) => e.Kind switch
    {
        CommentaryKind.Thrown => 100, CommentaryKind.SupportLost => 95, CommentaryKind.Click => 80,
        CommentaryKind.DragStarted => 70, CommentaryKind.WindowChanged => e.Related ? 65 : 10,
        CommentaryKind.Landed => 40, CommentaryKind.Jumped => 35, CommentaryKind.Idle => 30,
        CommentaryKind.ForegroundChurn => 25, _ => 20
    };
    private static double KindCooldown(CommentaryKind kind) => kind switch
    { CommentaryKind.Click => 40, CommentaryKind.Thrown => 45, CommentaryKind.CursorNear => 120,
        CommentaryKind.WindowChanged or CommentaryKind.ForegroundChurn => 90, CommentaryKind.Idle => 240, _ => 60 };
    private bool Eligible(CommentaryEvent e)
    {
        double t = now();
        return CanObserve && (!lastKind.TryGetValue(e.Kind, out var last) || t - last >= KindCooldown(e.Kind)) &&
            t - lastSpeech >= (Priority(e) >= 95 && Priority(e) > lastPriority ? options.ImportantCooldown : options.GlobalCooldown);
    }
    public void Receive(CommentaryEvent e)
    {
        if (!CanObserve) return;
        // Supersede relevant context, without letting unrelated window motion starve an idle comment.
        if (generation is not null)
        {
            bool sameWindow = e.Kind == CommentaryKind.WindowChanged && e.Handle == generatingEvent.Handle;
            bool leftSupport = generatingEvent.Related && e.Kind is CommentaryKind.Jumped or CommentaryKind.DragStarted or CommentaryKind.SupportLost;
            if (sameWindow || leftSupport || Priority(e) >= Priority(generatingEvent)) CancelPending();
            else return;
        }
        if (!Eligible(e)) return;
        if (e.Kind is CommentaryKind.Click or CommentaryKind.Thrown or CommentaryKind.SupportLost)
        { pending = null; Publish(e, Local(e), false); return; }
        if (pending is { } old && Priority(old.Event) > Priority(e)) return;
        if (pending is { } same && same.Event.Kind == e.Kind && same.Event.Handle == e.Handle)
            e = e with { Changes = e.Changes | same.Event.Changes };
        pending = (e, now()); // One coalesced event; never accumulate an activity history.
    }
    public void ObserveWindows(WindowMapSnapshot snapshot, nint support)
    {
        const WindowChangeKind relevant = WindowChangeKind.Appeared | WindowChangeKind.Disappeared |
            WindowChangeKind.Moved | WindowChangeKind.Resized | WindowChangeKind.Minimized | WindowChangeKind.Restored;
        foreach (var change in snapshot.Changes)
            if ((change.Kind & relevant) != 0)
                Receive(new(CommentaryKind.WindowChanged, change.Handle, change.Kind & relevant, change.Handle == support && support != 0));
    }
    public void ObserveActivity(ActivitySample sample)
    {
        double t = now();
        if (!CanObserve || !sample.Interactive || !double.IsFinite(sample.IdleSeconds) || sample.IdleSeconds < 0)
        { ResetObservation(); return; }
        if (sampleAt is not { } previous || t - previous is < 0 or > 5 || sample.IdleSeconds > previousIdle + t - previous + 2)
        {
            ResetObservation(); idleBaseline = t; foreground = sample.Foreground;
        }
        else
        {
            if (sample.IdleSeconds + .1 < previousIdle)
            {
                idleBaseline = t - sample.IdleSeconds; idleSpoken = false;
                if (pending?.Event.Kind == CommentaryKind.Idle || generation is not null && generatingEvent.Kind == CommentaryKind.Idle)
                    CancelPending();
            }
            if (sample.Foreground != 0 && foreground != 0 && sample.Foreground != foreground)
            {
                focusChanges.Enqueue(t);
                while (focusChanges.TryPeek(out var at) && t - at > 12) focusChanges.Dequeue();
                if (focusChanges.Count >= 6) { Receive(new(CommentaryKind.ForegroundChurn)); focusChanges.Clear(); }
            }
            if (!idleSpoken && Math.Min(sample.IdleSeconds, t - (idleBaseline ?? t)) >= options.IdleThreshold)
            { idleSpoken = true; Receive(new(CommentaryKind.Idle)); }
        }
        foreground = sample.Foreground; sampleAt = t; previousIdle = sample.IdleSeconds;
    }
    public async Task PulseAsync()
    {
        if (!CanObserve || generation is not null || pending is not { } item) return;
        double age = now() - item.At;
        double lifetime = item.Event.Kind == CommentaryKind.Idle ? options.IdleEventLifetime : options.EventLifetime;
        if (age > lifetime) { pending = null; return; }
        if (age < 1 || !Eligible(item.Event)) return;
        pending = null;
        var selection = settings();
        bool useAi = item.Event.Kind is CommentaryKind.Idle or CommentaryKind.WindowChanged or CommentaryKind.ForegroundChurn &&
            selection.Provider == ChatProviderKind.Ollama && !string.IsNullOrWhiteSpace(selection.Model) && now() - lastAi >= options.AiCooldown;
        if (!useAi) { Publish(item.Event, Local(item.Event), false); return; }
        using var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(item.Event.Kind == CommentaryKind.Idle ? options.IdleAiTimeout : options.AiTimeout));
        generation = cancellation; generatingEvent = item.Event; long version = ++epoch; lastAi = now();
        string? reply = null;
        try
        {
            var service = provider(selection);
            if (service is not null)
                reply = await service.GetReplyAsync(new(SmileyIdentity.SystemInstruction,
                    [new("user", $"Evento factual fornecido pelo aplicativo: {Fact(item.Event)}. Não há acesso à imagem da tela, conteúdo, títulos ou motivo da mudança. Use exclusivamente esse fato; não descreva a tela nem invente aplicativos, tarefas ou apresentações. Faça uma única fala espontânea natural de uma ou duas frases curtas, até 160 caracteres, sem explicar estas instruções. Não atribua mudanças de janela ao usuário.")], 128), cancellation.Token);
        }
        catch (Exception e) when (e is OperationCanceledException or ChatProviderException or System.Net.Http.HttpRequestException) { }
        finally { if (ReferenceEquals(generation, cancellation)) generation = null; }
        if (version != epoch || !CanObserve || now() - item.At > lifetime || !Eligible(item.Event)) return;
        var current = settings();
        if (current.Provider != selection.Provider || current.Model != selection.Model) return;
        string shortReply = Shorten(reply ?? "");
        Publish(item.Event, shortReply.Length == 0 ? Local(item.Event) : shortReply, shortReply.Length != 0);
    }
    private void Publish(CommentaryEvent e, string text, bool ai)
    {
        if (LastSpeech is { } last && last.Text == text) { text = Local(e); ai = false; }
        lastSpeech = now(); lastKind[e.Kind] = lastSpeech; lastPriority = Priority(e);
        LastSpeech = new(text, ai); speak(LastSpeech.Value);
    }
    private static string Fact(CommentaryEvent e) => e.Kind switch
    {
        CommentaryKind.Idle => "A sessão não recebe entrada há cerca de quatro minutos. Isso não indica se a tela mudou, quais programas estão em uso ou se a pessoa saiu.",
        CommentaryKind.ForegroundChurn => "A janela em primeiro plano mudou várias vezes em poucos segundos.",
        CommentaryKind.WindowChanged => $"Uma janela {(e.Related ? "que sustenta Smiley " : "")}teve estas alterações geométricas: {e.Changes}.",
        _ => "Smiley interagiu fisicamente com o ambiente."
    };
    private string Local(CommentaryEvent e)
    {
        string[] choices = e.Kind switch
        {
            CommentaryKind.Click => ["Oxe, que foi? Tem alguma coisa no meu capuz?", "Pode falar. Esse botão de cutucar funciona mesmo.", "Sim, eu existo. Não precisa conferir com o dedo."],
            CommentaryKind.DragStarted => ["Eita, mudança sem aviso prévio?", "Táxi de cursor. Que luxo.", "Pelo menos avisa o destino!"],
            CommentaryKind.Thrown => ["RAPAZ! EU TENHO PERNAS, SABIA?", "Entrega expressa de entidade cósmica!", "Freio! Cadê o freio desse negócio?!"],
            CommentaryKind.SupportLost => ["EI! CADÊ O CHÃO?!", "Meu apoio pediu demissão.", "Ah, ótimo. Agora o chão desaparece."],
            CommentaryKind.Landed => ["Pouso concluído. A dignidade vem depois.", "Chão firme. Finalmente.", "Tá tudo sob controle. Quase tudo."],
            CommentaryKind.Jumped => ["Licença, testando a gravidade.", "Um pulinho pra mudar de perspectiva.", "O capuz também é aerodinâmico."],
            CommentaryKind.CursorNear => ["Esse cursor tá curioso também?", "Chega aí. Sem me lançar, de preferência.", "Tô vendo esse cursor chegando perto."],
            CommentaryKind.Idle => ["Tu saiu e deixou o capuz de plantão?", "Silêncio por aqui. Vou fingir que é um retiro cósmico.", "Se foi buscar café, aceito um imaginário."],
            CommentaryKind.ForegroundChurn => ["As janelas tão dançando por aqui.", "Tanta troca de janela. Até meu capuz ficou tonto.", "Passeio rápido pelas janelas, hein?"],
            _ when e.Related => ["Meu apoio tá fazendo reforma?", "Essa janela virou transporte também.", "Chão com ajustes. Que tecnologia."],
            _ => ["O cenário das janelas mudou de novo.", "Uma janela mudou por aqui. Vida que segue.", "Esse desktop não para quieto." ]
        };
        int index = variants.GetValueOrDefault(e.Kind); variants[e.Kind] = (index + 1) % choices.Length;
        return choices[index % choices.Length];
    }
    public static string Shorten(string text)
    {
        text = Regex.Replace(text, @"\s+", " ").Trim().Trim('"', '“', '”');
        var sentences = Regex.Matches(text, @"[^.!?]+[.!?]+(?:\s|$)").Select(m => m.Value.Trim()).Take(2).ToArray();
        string result = "";
        foreach (var sentence in sentences)
        {
            string candidate = result.Length == 0 ? sentence : result + " " + sentence;
            if (new StringInfo(candidate).LengthInTextElements > 180) break;
            result = candidate;
        }
        if (result.Length > 0) return result;
        var elements = StringInfo.ParseCombiningCharacters(text);
        if (elements.Length <= 180) return text;
        string cut = text[..elements[177]];
        int space = cut.LastIndexOf(' ');
        return (space > cut.Length / 2 ? cut[..space] : cut).TrimEnd() + "…";
    }
    public void Dispose() { disposed = true; ResetObservation(); }
}
