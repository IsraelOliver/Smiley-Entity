namespace Smiley;

public enum BehaviorAction { Idle, Wander, Run, Hop, Curious, Flee, Explore }
public enum MovementPreference { Automatic, Walking, Running }
public readonly record struct CursorPosition(double X, double Y);
public readonly record struct CursorObservation(double DistanceDips, int Side, bool Approaching, bool InsideClickZone);

public sealed class SmileyBehaviorOptions
{
    public double IdleMin { get; init; } = 6;
    public double IdleMax { get; init; } = 16;
    public double WanderMin { get; init; } = 2;
    public double WanderMax { get; init; } = 4;
    public double RunMin { get; init; } = 1;
    public double RunMax { get; init; } = 2;
    public double IdleWeight { get; init; } = 55;
    public double WanderWeight { get; init; } = 25;
    public double RunWeight { get; init; } = 10;
    public double HopWeight { get; init; } = 10;
    public double EvaluationInterval { get; init; } = 0.1;
    public double InterestRadius { get; init; } = 120;
    public double CuriousMin { get; init; } = 1;
    public double CuriousMax { get; init; } = 2;
    public double FleeProbability { get; init; } = 0.25;
    public double FleeCooldown { get; init; } = 8;
    public double CursorCooldown { get; init; } = 5;
    public double HopCooldown { get; init; } = 20;
    public double ManualQuietSeconds { get; init; } = 2;
    public double ClickZoneMargin { get; init; } = 24;
    public double MinimumTravel { get; init; } = 24;
    public double HopTopMargin { get; init; } = 12;
    public double NavigationProbability { get; init; } = 0.3;
    public double NavigationCooldown { get; init; } = 8;
    public double FailedTargetCooldown { get; init; } = 30;

    internal void Validate()
    {
        double[] positive = [IdleMin, IdleMax, WanderMin, WanderMax, RunMin, RunMax, EvaluationInterval,
            InterestRadius, CuriousMin, CuriousMax, FleeCooldown, CursorCooldown, HopCooldown, ManualQuietSeconds, NavigationCooldown, FailedTargetCooldown];
        double[] nonnegative = [IdleWeight, WanderWeight, RunWeight, HopWeight, ClickZoneMargin, MinimumTravel, HopTopMargin];
        if (positive.Any(x => !double.IsFinite(x) || x <= 0) || nonnegative.Any(x => !double.IsFinite(x) || x < 0) ||
            IdleMax < IdleMin || WanderMax < WanderMin || RunMax < RunMin || CuriousMax < CuriousMin ||
            !double.IsFinite(IdleWeight + WanderWeight + RunWeight + HopWeight) || IdleWeight + WanderWeight + RunWeight + HopWeight <= 0 ||
            !double.IsFinite(NavigationProbability) || NavigationProbability < 0 || NavigationProbability > 1 ||
            !double.IsFinite(FleeProbability) || FleeProbability < 0 || FleeProbability > 0.25)
            throw new ArgumentOutOfRangeException(nameof(SmileyBehaviorOptions), "Intervalos/probabilidades inválidos; fuga deve ficar entre 0 e 25%.");
    }
}

// Decisions run on one 10 Hz timeline inside the existing render update. Motion,
// collisions, animation and ballistic integration remain exclusively in SmileyController.
public sealed class SmileyBehaviorController
{
    private readonly SmileyController controller;
    private readonly SmileyBehaviorOptions options;
    private readonly Random random;
    private double untilEvaluation, remaining, quiet, fleeCooldown, cursorCooldown, hopCooldown;
    private bool near, hasCursorHistory;
    private CursorPosition previousCursor;
    private double previousDistance;
    private (MovementMode Mode, int Direction, double Seconds)? resume;
    private readonly SmileyNavigationPlanner navigation = new();
    private WindowPlatformMap navigationMap = WindowPlatformMap.Unavailable;
    private bool navigationFresh;
    private double decisionTime, navigationCooldown;
    private readonly Dictionary<nint, double> retryAfter = new();
    private readonly List<nint> expiredRetries = new();
    private NavigationPlan? navigationPlan;
    private NavigationStage navigationStage;
    private double requestedRemaining;
    public bool HasRequestedAction => requestedRemaining > 1e-9;
    private long plannedVersion;
    private WorkArea plannedArea;
    private double plannedWidth, plannedScaleX, plannedScaleY;
    public bool ExploreWindows { get; private set; }
    public NavigationPlan? NavigationPlan => navigationPlan;
    public NavigationStage? NavigationStage => navigationPlan is null ? null : navigationStage;
    public NavigationDiagnostic NavigationStatus { get; private set; } = new("Exploração desativada.", 0, 0);
    public BehaviorAction CurrentAction { get; private set; } = BehaviorAction.Idle;
    public BehaviorAction LastDecision { get; private set; } = BehaviorAction.Idle;
    public int DecisionCount { get; private set; }
    public bool Enabled { get; private set; } = true;
    public MovementPreference Preference { get; private set; } = MovementPreference.Automatic;
    public CursorObservation? Cursor { get; private set; }

    public SmileyBehaviorController(SmileyController controller, SmileyBehaviorOptions? options = null, Random? random = null)
    {
        this.controller = controller;
        this.options = options ?? new(); this.options.Validate();
        this.random = random ?? new();
        controller.UseExternalBehavior();
        untilEvaluation = this.options.EvaluationInterval;
        EnterRest();
    }

    private double Between(double min, double max) => min + random.NextDouble() * (max - min);
    private void EnterRest()
    {
        CurrentAction = BehaviorAction.Idle; resume = null;
        remaining = Between(options.IdleMin, options.IdleMax);
        controller.TryRest();
    }

    public void SetEnabled(bool enabled)
    {
        if (Enabled == enabled) return;
        if (!enabled) CancelRequestedAction();
        if (!enabled) CancelNavigation("Autonomia desativada.");
        Enabled = enabled; resume = null; ResetCursor();
        if (controller.State == SmileyState.Idle) EnterRest();
    }

    public void SetPreference(MovementPreference preference)
    {
        if (!Enum.IsDefined(preference)) throw new ArgumentOutOfRangeException(nameof(preference));
        CancelRequestedAction();
        Preference = preference;
        if (ExploreWindows) { CancelNavigation("Modo de deslocamento alterado."); controller.TryRest(); }
        if (preference != MovementPreference.Automatic)
        {
            var mode = preference == MovementPreference.Running ? MovementMode.Running : MovementMode.Walking;
            controller.SetMovementMode(mode);
            if (resume is { } saved) resume = (mode, saved.Direction, saved.Seconds);
        }
    }

    public void NotifyManualInteraction()
    {
        CancelRequestedAction();
        CancelNavigation("Interação manual tem prioridade.");
        quiet = options.ManualQuietSeconds; resume = null;
        CurrentAction = BehaviorAction.Idle;
        remaining = Between(options.IdleMin, options.IdleMax);
        cursorCooldown = Math.Max(cursorCooldown, options.CursorCooldown);
        ResetCursor();
    }

    private void ResetCursor() { near = hasCursorHistory = false; Cursor = null; }
    public void SetNavigationMap(WindowPlatformMap map, bool fresh) { navigationMap = map; navigationFresh = fresh && map.IsAvailable; }
    public void SetExploreWindows(bool enabled)
    {
        if (ExploreWindows == enabled) return;
        if (CurrentAction == BehaviorAction.Explore) CancelRequestedAction();
        CancelNavigation(enabled ? "Exploração ativada; aguardando decisão." : "Exploração desativada.");
        ExploreWindows = enabled;
        // Do not keep an old unbounded wander running when enabling safe exploration.
        if (enabled) { controller.TryRest(); EnterRest(); }
    }

    private void CancelNavigation(string reason, bool failed = false)
    {
        if (navigationPlan is { } plan)
        {
            if (failed) RememberFailure(plan.Target.Handle);
            controller.TryRest();
            navigationPlan = null; navigationCooldown = options.NavigationCooldown;
            CurrentAction = BehaviorAction.Idle; resume = null;
            remaining = options.IdleMin;
        }
        NavigationStatus = new(reason, controller.ActiveSupport?.Handle ?? 0, 0);
    }
    private void RememberFailure(nint target)
    {
        expiredRetries.Clear();
        foreach (var entry in retryAfter) if (entry.Value <= decisionTime) expiredRetries.Add(entry.Key);
        foreach (var handle in expiredRetries) retryAfter.Remove(handle);
        if (retryAfter.Count >= 32) retryAfter.Clear(); // Bounded transient cooldowns, never persisted.
        retryAfter[target] = decisionTime + options.FailedTargetCooldown;
    }
    private bool Blocked(nint handle) => retryAfter.TryGetValue(handle, out var until) && until > decisionTime;
    private double NavigationSpeed => Preference == MovementPreference.Walking ? controller.MotionLimits.WalkSpeed : controller.MotionLimits.RunSpeed;

    // Explicit high-level requests reuse this controller's existing movement/navigation state.
    // Their finite reservation prevents a random decision from immediately replacing the request.
    public SmileyActionResult TryRequestAction(SmileyActionType type, SmileyActionEnvironment environment)
    {
        if (!Enum.IsDefined(type)) return new(false, "Tipo de ação não permitido.");
        if (type == SmileyActionType.None) return new(true, "");
        if (controller.Paused) return new(false, "O Smiley está pausado.");
        if (!Enabled) return new(false, "A autonomia está desativada; a IA não vai reativá-la.");
        if (!controller.IsGrounded) return new(false, "O Smiley está no ar; aguarde a aterrissagem.");
        if (quiet > 1e-9 || controller.State is SmileyState.Dragging or SmileyState.Reacting)
            return new(false, "Uma interação direta tem prioridade agora.");
        var (area, width, sx, sy) = environment;
        if (!double.IsFinite(width) || width <= 0 || !double.IsFinite(sx) || sx <= 0 || !double.IsFinite(sy) || sy <= 0 ||
            !double.IsFinite(area.Left) || !double.IsFinite(area.Right) || !double.IsFinite(area.Top) || !double.IsFinite(area.Bottom) ||
            area.Right <= area.Left || area.Bottom <= area.Top)
            return new(false, "Geometria de execução indisponível.");
        if (type == SmileyActionType.Explore && !ExploreWindows)
            return new(false, "A exploração de janelas está desativada.");
        CancelRequestedAction(); CancelNavigation("Novo pedido explícito de ação."); resume = null;
        if (type == SmileyActionType.Idle)
        {
            if (!controller.TryRest()) return new(false, "Não foi possível repousar agora.");
            CurrentAction = BehaviorAction.Idle; requestedRemaining = Math.Clamp(options.IdleMin, 1, 10);
            return new(true, $"Repouso iniciado por até {requestedRemaining:0.#} s.");
        }
        if (type == SmileyActionType.Jump)
        {
            if (controller.Y - controller.JumpHeight * sy < area.Top + options.HopTopMargin * sy || !controller.TryJump(sy))
                return new(false, "Salto recusado: apoio ou espaço vertical insuficiente.");
            CurrentAction = BehaviorAction.Hop; hopCooldown = options.HopCooldown;
            return new(true, "Salto iniciado.");
        }
        if (type == SmileyActionType.Explore)
        {
            if (navigationCooldown > 1e-9) return new(false, "A navegação está em intervalo de recuperação.");
            if (!StartNavigation(area, width, sx, sy) || navigationPlan is null)
                return new(false, "Exploração não iniciada: " + NavigationStatus.Status);
            requestedRemaining = 15; // One existing plan only, never an endless exploration loop.
            return new(true, "Exploração iniciada; a chegada ainda depende da física.");
        }
        if (!navigation.TrySafeRange(controller, area, width, sx, out var left, out var right))
            return new(false, "Não há espaço seguro para esse deslocamento.");
        bool run = type == SmileyActionType.Run;
        var mode = run ? MovementMode.Running : MovementMode.Walking;
        double duration = run ? Math.Clamp(options.RunMin, .5, 2) : Math.Clamp(options.WanderMin, .5, 4);
        double speed = (run ? controller.MotionLimits.RunSpeed : controller.MotionLimits.WalkSpeed) * sx;
        int direction = controller.Direction;
        double room = direction > 0 ? right - controller.X : controller.X - left;
        if (room < options.MinimumTravel * sx) { direction = -direction; room = direction > 0 ? right - controller.X : controller.X - left; }
        if (room < options.MinimumTravel * sx) return new(false, "Não há espaço seguro para esse deslocamento.");
        duration = Math.Min(duration, room / speed);
        if (!controller.TryMove(mode, direction, duration)) return new(false, "Deslocamento recusado pela física.");
        CurrentAction = run ? BehaviorAction.Run : BehaviorAction.Wander; requestedRemaining = duration;
        return new(true, (run ? "Corrida" : "Caminhada") + $" iniciada por até {duration:0.#} s.");
    }
    public void CancelRequestedAction()
    {
        if (!HasRequestedAction) return;
        requestedRemaining = 0;
        CancelNavigation("Pedido explícito encerrado.");
        controller.TryRest(); EnterRest();
    }

    private bool StartNavigation(WorkArea area, double width, double sx, double sy)
    {
        if (!navigation.TryChoose(navigationMap, navigationFresh, controller, area, width, sx, sy, NavigationSpeed, Blocked, out var plan, out var reason))
        { NavigationStatus = new(reason, controller.ActiveSupport?.Handle ?? 0, 0); return false; }
        navigationPlan = plan; navigationStage = Smiley.NavigationStage.Approaching;
        SavePlanEnvironment(area, width, sx, sy); CurrentAction = LastDecision = BehaviorAction.Explore;
        AdvanceNavigation(area, width, sx, sy); return true;
    }

    private void AdvanceNavigation(WorkArea area, double width, double sx, double sy)
    {
        if (navigationPlan is not { } plan) return;
        if (!navigationFresh) { CancelNavigation("Plano cancelado: mapa sem coleta saudável.", failed: true); return; }
        if ((controller.ActiveSupport?.Handle ?? 0) != plan.OriginHandle)
        { CancelNavigation("A superfície de origem mudou.", failed: true); return; }
        bool newSnapshot = plannedVersion != navigationMap.Source.Version;
        if (newSnapshot && navigationMap.Source.Changes.Any(c => (c.Handle == plan.Target.Handle || c.Handle == plan.OriginHandle) && c.Kind.HasFlag(WindowChangeKind.Disappeared)))
        { CancelNavigation("Uma janela do plano foi fechada ou substituída.", failed: true); return; }
        bool changed = newSnapshot && !navigationMap.Platforms.Any(p => SmileyNavigationPlanner.SameSurface(p, plan.Target)) ||
            (controller.ActiveSupport?.SourceBounds ?? default) != plan.OriginBounds || Math.Abs(controller.FeetY - plan.OriginY) > 1e-6 ||
            controller.ActiveSupport is { } origin && plan.OriginSegment is { } oldOrigin && !SmileyNavigationPlanner.SameSurface(origin.Segment, oldOrigin) ||
            area != plannedArea || width != plannedWidth || sx != plannedScaleX || sy != plannedScaleY;
        plannedVersion = navigationMap.Source.Version;
        if (changed)
        {
            controller.TryRest();
            if (!navigation.TryChoose(navigationMap, navigationFresh, controller, area, width, sx, sy, NavigationSpeed, Blocked,
                out plan, out var rejection, plan.Target.Handle))
            { CancelNavigation("Replanejamento: " + rejection, failed: true); return; }
            navigationPlan = plan; SavePlanEnvironment(area, width, sx, sy);
        }
        double distance = plan.DepartureX - controller.X;
        NavigationStatus = new("Alcançável: aproximando do ponto de partida.", plan.OriginHandle, plan.Target.Handle, plan.DepartureX, plan.LandingX, plan.Target.Y);
        if (Math.Abs(distance) > 1e-6)
        {
            if (!controller.IsMoving) controller.TryMove(MovementMode.Walking, Math.Sign(distance), Math.Abs(distance) / (controller.MotionLimits.WalkSpeed * sx));
            return;
        }
        // Validate once more at launch, including intervening surfaces and current body position.
        if (!navigation.TryChoose(navigationMap, navigationFresh, controller, area, width, sx, sy, NavigationSpeed, Blocked,
            out plan, out var reason, plan.Target.Handle) || Math.Abs(plan.DepartureX - controller.X) > 1e-6)
        { CancelNavigation("Partida cancelada: " + reason, failed: true); return; }
        controller.TryRest();
        if (!controller.TryDirectedJump(plan.HorizontalVelocity, sx, sy))
        { CancelNavigation("Solicitação de salto recusada pela física.", failed: true); return; }
        navigationPlan = plan; navigationStage = Smiley.NavigationStage.Flying;
        NavigationStatus = new("Salto em curso; aguardando aterrissagem real.", plan.OriginHandle, plan.Target.Handle, plan.DepartureX, plan.LandingX, plan.Target.Y);
    }
    private void SavePlanEnvironment(WorkArea area, double width, double sx, double sy)
    {
        plannedVersion = navigationMap.Source.Version; plannedArea = area; plannedWidth = width; plannedScaleX = sx; plannedScaleY = sy;
    }
    public bool NeedsCursorSample(double elapsed) => Enabled && !controller.Paused && untilEvaluation <= elapsed + 1e-9;

    public void Tick(double seconds, WorkArea area, double width, double height, double scaleX = 1, double scaleY = 1,
        CursorPosition? cursor = null, double? footOffset = null)
    {
        if (!double.IsFinite(seconds) || seconds <= 0 || !double.IsFinite(scaleX) || scaleX <= 0 || !double.IsFinite(scaleY) || scaleY <= 0 || controller.Paused) return;
        while (seconds > 1e-9)
        {
            double step = Math.Min(seconds, untilEvaluation);
            controller.Tick(step, area, width, height, scaleX, scaleY, footOffset);
            seconds -= step; untilEvaluation -= step;
            if (untilEvaluation > 1e-9) continue;
            Evaluate(area, width, height, scaleX, scaleY, cursor);
            untilEvaluation = options.EvaluationInterval;
        }
    }

    private void Evaluate(WorkArea area, double width, double height, double sx, double sy, CursorPosition? cursor)
    {
        double dt = options.EvaluationInterval;
        decisionTime += dt;
        navigationCooldown = Math.Max(0, navigationCooldown - dt);
        fleeCooldown = Math.Max(0, fleeCooldown - dt);
        cursorCooldown = Math.Max(0, cursorCooldown - dt);
        hopCooldown = Math.Max(0, hopCooldown - dt);
        bool encounter = ObserveCursor(cursor, width, height, sx, sy);
        if (HasRequestedAction)
        {
            requestedRemaining = Math.Max(0, requestedRemaining - dt);
            if (!HasRequestedAction)
            { CancelNavigation("Tempo do pedido explícito encerrado."); controller.TryRest(); EnterRest(); }
        }
        if (controller.State is SmileyState.Dragging or SmileyState.Reacting)
        { CancelRequestedAction(); if (navigationPlan is not null) CancelNavigation("Reação ou arrasto interrompeu o plano."); return; }
        if (navigationPlan is { } flight && navigationStage == Smiley.NavigationStage.Flying)
        {
            if (!controller.IsGrounded) return; // Never flee or steer an existing flight.
            bool landed = controller.ActiveSupport?.Handle == flight.Target.Handle;
            if (landed && flight.Target.Handle == flight.OriginHandle && controller.ActiveSupport is { } actual)
            {
                double shift = actual.SourceBounds.Left - flight.OriginBounds.Left, center = controller.X + width / 2;
                landed = center >= flight.Target.Left + shift && center <= flight.Target.Right + shift;
            }
            if (!landed) RememberFailure(flight.Target.Handle);
            navigationPlan = null; navigationCooldown = options.NavigationCooldown; requestedRemaining = 0;
            NavigationStatus = new(landed ? "Aterrissagem confirmada no destino." : "Destino não alcançado; tentativa em cooldown.", controller.ActiveSupport?.Handle ?? 0, flight.Target.Handle);
            EnterRest(); return;
        }
        if (!controller.IsGrounded)
        { if (navigationPlan is not null) CancelNavigation("Condição aérea não planejada.", failed: true); return; }
        if (quiet > 1e-9)
        {
            controller.TryRest(); quiet = Math.Max(0, quiet - dt); return;
        }
        if (!Enabled)
        {
            if (controller.State == SmileyState.Idle) controller.TryRest();
            return; // Finite moves finish in the motion controller; physics remains active.
        }
        if (HasRequestedAction)
        {
            if (navigationPlan is not null) { AdvanceNavigation(area, width, sx, sy); return; }
            if (CurrentAction == BehaviorAction.Idle) { controller.TryRest(); return; }
            double speed = controller.MovementMode == MovementMode.Running ? controller.MotionLimits.RunSpeed : controller.MotionLimits.WalkSpeed;
            double endpoint = controller.X + controller.Direction * speed * sx * controller.ActionTimeRemaining;
            if (!controller.IsMoving || !navigation.TrySafeRange(controller, area, width, sx, out var left, out var right) || endpoint < left - 1e-6 || endpoint > right + 1e-6)
                CancelRequestedAction();
            return;
        }
        if (encounter && cursorCooldown <= 1e-9 && CurrentAction is not (BehaviorAction.Curious or BehaviorAction.Flee))
        {
            if (navigationPlan is not null) CancelNavigation("Reação ao cursor tem prioridade.");
            cursorCooldown = options.CursorCooldown;
            int away = Cursor!.Value.Side < 0 ? 1 : -1;
            bool canFlee = Cursor.Value.Approaching && !Cursor.Value.InsideClickZone && fleeCooldown <= 1e-9 &&
                Room(away, area, width) >= options.MinimumTravel * sx;
            if (canFlee && random.NextDouble() < options.FleeProbability)
            {
                resume = null; fleeCooldown = options.FleeCooldown;
                var mode = Preference == MovementPreference.Walking ? MovementMode.Walking : MovementMode.Running;
                controller.TryMove(mode, away, Between(options.RunMin, options.RunMax));
                CurrentAction = BehaviorAction.Flee; return;
            }
            resume = controller.IsMoving ? (controller.MovementMode, controller.Direction, controller.ActionTimeRemaining) : null;
            controller.TryRest(Pose.Sceptical);
            CurrentAction = BehaviorAction.Curious; remaining = Between(options.CuriousMin, options.CuriousMax); return;
        }
        if (CurrentAction == BehaviorAction.Curious)
        {
            remaining -= dt;
            if (remaining > 1e-9) return;
            if (resume is { } saved && controller.TryMove(saved.Mode, saved.Direction, saved.Seconds))
            {
                CurrentAction = saved.Mode == MovementMode.Running ? BehaviorAction.Run : BehaviorAction.Wander;
                resume = null;
            }
            else EnterRest();
            return;
        }
        if (navigationPlan is not null) { AdvanceNavigation(area, width, sx, sy); return; }
        if (CurrentAction != BehaviorAction.Idle)
        {
            if (ExploreWindows && controller.IsMoving && CurrentAction is BehaviorAction.Wander or BehaviorAction.Run)
            {
                double speed = controller.MovementMode == MovementMode.Running ? controller.MotionLimits.RunSpeed : controller.MotionLimits.WalkSpeed;
                double endpoint = controller.X + controller.Direction * speed * sx * controller.ActionTimeRemaining;
                if (!navigationFresh || !navigation.TrySafeRange(controller, area, width, sx, out var left, out var right) || endpoint < left || endpoint > right)
                { controller.TryRest(); EnterRest(); return; }
            }
            if (!controller.IsMoving) EnterRest();
            return;
        }
        remaining -= dt;
        if (remaining > 1e-9) return;
        DecisionCount++;
        if (ExploreWindows && navigationFresh && navigationCooldown <= 1e-9 && random.NextDouble() < options.NavigationProbability && StartNavigation(area, width, sx, sy)) return;
        var action = SelectAction(); LastDecision = action;
        if (action == BehaviorAction.Idle) { EnterRest(); return; }
        if (action == BehaviorAction.Hop)
        {
            if (hopCooldown <= 1e-9 && controller.Y - controller.JumpHeight * sy >= area.Top + options.HopTopMargin * sy && controller.TryJump(sy))
            { CurrentAction = BehaviorAction.Hop; hopCooldown = options.HopCooldown; }
            else EnterRest();
            return;
        }
        var movement = action == BehaviorAction.Run ? MovementMode.Running : MovementMode.Walking;
        if (ExploreWindows)
        {
            if (!navigationFresh || !navigation.TryLocalDestination(controller, area, width, sx, random.NextDouble(), out var destination) || Math.Abs(destination - controller.X) < options.MinimumTravel * sx)
            { EnterRest(); return; }
            double speed = movement == MovementMode.Running ? controller.MotionLimits.RunSpeed : controller.MotionLimits.WalkSpeed;
            controller.TryMove(movement, Math.Sign(destination - controller.X), Math.Abs(destination - controller.X) / (speed * sx));
            CurrentAction = action; return;
        }
        int direction = random.Next(2) == 0 ? -1 : 1;
        if (Room(direction, area, width) < options.MinimumTravel * sx) direction = -direction;
        if (Room(direction, area, width) < options.MinimumTravel * sx) { EnterRest(); return; }
        double duration = movement == MovementMode.Running ? Between(options.RunMin, options.RunMax) : Between(options.WanderMin, options.WanderMax);
        controller.TryMove(movement, direction, duration); CurrentAction = action;
    }

    internal BehaviorAction SelectAction()
    {
        double roll = random.NextDouble() * (options.IdleWeight + options.WanderWeight + options.RunWeight + options.HopWeight);
        BehaviorAction action = roll < options.IdleWeight ? BehaviorAction.Idle
            : roll < options.IdleWeight + options.WanderWeight ? BehaviorAction.Wander
            : roll < options.IdleWeight + options.WanderWeight + options.RunWeight ? BehaviorAction.Run : BehaviorAction.Hop;
        return Preference == MovementPreference.Automatic || action == BehaviorAction.Idle ? action
            : Preference == MovementPreference.Walking ? BehaviorAction.Wander : BehaviorAction.Run;
    }

    private double Room(int direction, WorkArea area, double width) => direction > 0 ? area.Right - width - controller.X : controller.X - area.Left;

    private bool ObserveCursor(CursorPosition? cursor, double width, double height, double sx, double sy)
    {
        if (!Enabled || cursor is not { } p || !double.IsFinite(p.X) || !double.IsFinite(p.Y)) { ResetCursor(); return false; }
        double dx = (p.X - controller.X - width / 2) / sx, dy = (p.Y - controller.Y - height / 2) / sy;
        double gapX = Math.Max(0, Math.Abs(dx) - width / (2 * sx));
        double gapY = Math.Max(0, Math.Abs(dy) - height / (2 * sy));
        double distance = Math.Sqrt(gapX * gapX + gapY * gapY);
        bool moved = hasCursorHistory && (Math.Abs(p.X - previousCursor.X) / sx > 1 || Math.Abs(p.Y - previousCursor.Y) / sy > 1);
        Cursor = new(distance, dx < 0 ? -1 : 1, moved && distance < previousDistance - 2, gapX <= options.ClickZoneMargin && gapY <= options.ClickZoneMargin);
        bool encounter = !near && distance <= options.InterestRadius;
        if (distance > options.InterestRadius * 1.35) near = false;
        else if (distance <= options.InterestRadius) near = true;
        hasCursorHistory = true; previousCursor = p; previousDistance = distance;
        return encounter;
    }
}
