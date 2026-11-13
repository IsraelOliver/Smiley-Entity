namespace Smiley;

// Application-owned allowlist bridge, independent of chat controls and model providers.
// All calls belong to the same UI dispatcher as the existing behaviour/physics controllers.
public sealed class SmileyActionDispatcher
{
    private readonly SmileyController body;
    private readonly SmileyBehaviorController behavior;
    private readonly Func<SmileyActionEnvironment> environment;
    private readonly Func<bool> interactionBusy;
    public bool Enabled { get; private set; }
    public long Revision { get; private set; }
    public SmileyActionDispatcher(SmileyController body, SmileyBehaviorController behavior,
        Func<SmileyActionEnvironment> environment, Func<bool>? interactionBusy = null)
    { this.body = body; this.behavior = behavior; this.environment = environment; this.interactionBusy = interactionBusy ?? (() => false); }
    public void SetEnabled(bool enabled) { Enabled = enabled; Interrupt(); }
    public void Interrupt() { Revision++; behavior.CancelRequestedAction(); }
    public string DescribeState() => $"Estado factual: pausa={body.Paused}; no chão={body.IsGrounded}; interação prioritária={interactionBusy()}; autonomia={behavior.Enabled}; exploração habilitada={behavior.ExploreWindows}. Não altere essas opções; escolha none se impedirem a ação.";
    public SmileyActionResult Dispatch(SmileyActionRequest request, long expectedRevision)
    {
        if (!Enum.IsDefined(request.Type)) return new(false, "Tipo de ação não permitido.");
        if (request.Type == SmileyActionType.None) return new(true, "");
        if (!Enabled) return new(false, "Ações por IA estão desativadas.");
        if (expectedRevision != Revision) return new(false, "Pedido descartado: houve uma nova interação ou alteração de configuração.");
        if (interactionBusy()) return new(false, "Uma interação direta tem prioridade agora.");
        if (body.Paused) return new(false, "O Smiley está pausado.");
        if (!body.IsGrounded) return new(false, "O Smiley está no ar; aguarde a aterrissagem.");
        if (body.State is SmileyState.Dragging or SmileyState.Reacting) return new(false, "Uma interação direta está em andamento.");
        return behavior.TryRequestAction(request.Type, environment());
    }
}
