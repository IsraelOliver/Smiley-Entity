namespace Smiley;

public enum SmileyState { Idle, Walking, Running, Reacting, Dragging }
public enum MovementMode { Walking, Running }
public enum PhysicalState { Grounded, Rising, Falling, Suspended }
public enum Pose { Neutral, Running, Startled, Frowning, Sceptical, Falling }
public readonly record struct WorkArea(double Left, double Top, double Right, double Bottom);
public readonly record struct SmileyMotionLimits(double Gravity, double JumpSpeed, double WalkSpeed, double RunSpeed, double FootWidth, double MinimumFootOverlap);

public sealed class SmileyOptions
{
    public double Speed { get; init; } = 65; // DIPs/second; scaled by monitor DPI in the view.
    public double RunSpeed { get; init; } = 140;
    public double WalkFramesPerSecond { get; init; } = 4;
    public double RunFramesPerSecond { get; init; } = 8;
    public double IdleMin { get; init; } = 3;
    public double IdleMax { get; init; } = 7;
    public double WalkMin { get; init; } = 2;
    public double WalkMax { get; init; } = 5;
    public double ReactionSeconds { get; init; } = 1.1;
    public double Gravity { get; init; } = 1100; // DIPs/s², converted at the physics boundary.
    public double JumpSpeed { get; init; } = 460; // Positive magnitude, DIPs/s upwards.
    public double ThrowMinimumSpeed { get; init; } = 180; // DIPs/s; gentle releases just drop.
    public double ThrowMaximumSpeed { get; init; } = 1100;
    public double FootSupportWidth { get; init; } = 24; // DIPs, independent of sprite silhouette.
    public double MinimumFootOverlap { get; init; } = 8;
    public double ReleaseContactTolerance { get; init; } = 2;
}

public sealed class SmileyController
{
    public const int FramesPerCycle = 8;
    private readonly Random random;
    private readonly SmileyOptions options;
    private double remaining;
    private double animationSeconds;
    private bool hasEnvironment;
    private double floorY, scaleX = 1, scaleY = 1;
    private WorkArea area;
    private double windowWidth;
    private double footOffset;
    private WindowPlatformMap platforms = WindowPlatformMap.Unavailable;
    private WindowPlatformMap? supportMap;
    public WindowSupport? ActiveSupport { get; private set; }
    public double FeetY => Y + footOffset;
    public double MinimumPlatformWidth => options.FootSupportWidth;
    public SmileyMotionLimits MotionLimits => new(options.Gravity, options.JumpSpeed, options.Speed, options.RunSpeed, options.FootSupportWidth, options.MinimumFootOverlap);
    private bool externalBehavior;
    public double ActionTimeRemaining => remaining;
    public double JumpHeight => options.JumpSpeed * options.JumpSpeed / (2 * options.Gravity);
    public double VelocityX { get; private set; } // Physical pixels/s, like X/Y.
    public double VelocityY { get; private set; }
    public bool IsGrounded { get; private set; } = true;
    public bool CanJump => hasEnvironment && IsGrounded && !Paused && State != SmileyState.Dragging;
    public PhysicalState PhysicalState => State == SmileyState.Dragging ? PhysicalState.Suspended
        : IsGrounded ? PhysicalState.Grounded : VelocityY < 0 ? PhysicalState.Rising : PhysicalState.Falling;
    public Pose DisplayPose => !IsGrounded && State != SmileyState.Reacting
        ? (VelocityY > 0 ? Pose.Falling : Pose.Startled) : Pose;
    public MovementMode MovementMode { get; private set; } = MovementMode.Walking;
    private bool HasLocomotion => State is SmileyState.Walking or SmileyState.Running;
    public bool IsMoving => IsGrounded && HasLocomotion;
    public int AnimationFrame => IsMoving
        ? (int)(Math.Floor(animationSeconds * (State == SmileyState.Running ? options.RunFramesPerSecond : options.WalkFramesPerSecond) + 1e-9) % FramesPerCycle) : 0;
    public SmileyState State { get; private set; } = SmileyState.Idle;
    public Pose Pose { get; private set; } = Pose.Neutral;
    public bool Paused { get; private set; }
    public int Direction { get; private set; } = 1;
    public double X { get; private set; }
    public double Y { get; private set; }

    public SmileyController(SmileyOptions? options = null, Random? random = null)
    {
        this.options = options ?? new();
        if (!double.IsFinite(this.options.Speed) || this.options.Speed <= 0 ||
            !double.IsFinite(this.options.RunSpeed) || this.options.RunSpeed <= 0 ||
            !double.IsFinite(this.options.WalkFramesPerSecond) || this.options.WalkFramesPerSecond <= 0 ||
            !double.IsFinite(this.options.RunFramesPerSecond) || this.options.RunFramesPerSecond <= 0 ||
            !double.IsFinite(this.options.IdleMin) || this.options.IdleMin <= 0 ||
            !double.IsFinite(this.options.IdleMax) || this.options.IdleMax < this.options.IdleMin ||
            !double.IsFinite(this.options.WalkMin) || this.options.WalkMin <= 0 ||
            !double.IsFinite(this.options.WalkMax) || this.options.WalkMax < this.options.WalkMin ||
            !double.IsFinite(this.options.ReactionSeconds) || this.options.ReactionSeconds <= 0 ||
            !double.IsFinite(this.options.Gravity) || this.options.Gravity <= 0 ||
            !double.IsFinite(this.options.JumpSpeed) || this.options.JumpSpeed <= 0 ||
            !double.IsFinite(this.options.ThrowMinimumSpeed) || this.options.ThrowMinimumSpeed < 0 ||
            !double.IsFinite(this.options.ThrowMaximumSpeed) || this.options.ThrowMaximumSpeed <= 0 ||
            this.options.ThrowMaximumSpeed < this.options.ThrowMinimumSpeed ||
            !double.IsFinite(this.options.FootSupportWidth) || this.options.FootSupportWidth <= 0 ||
            !double.IsFinite(this.options.MinimumFootOverlap) || this.options.MinimumFootOverlap <= 0 || this.options.MinimumFootOverlap > this.options.FootSupportWidth ||
            !double.IsFinite(this.options.ReleaseContactTolerance) || this.options.ReleaseContactTolerance < 0)
            throw new ArgumentOutOfRangeException(nameof(options), "Velocidades, frequências e durações devem ser finitas e positivas; intervalos devem estar em ordem.");
        this.random = random ?? new();
        Rest();
    }

    private double Between(double min, double max) => min + random.NextDouble() * (max - min);
    private void Rest()
    {
        State = SmileyState.Idle;
        Pose = Pose.Neutral;
        animationSeconds = 0;
        if (IsGrounded) VelocityX = 0;
        remaining = Between(options.IdleMin, options.IdleMax);
    }

    // All world positions, sizes and velocities are physical pixels. DIPs enter only
    // through options and are converted with the current monitor scale in Tick/TryJump.
    public void UpdateEnvironment(WorkArea area, double width, double height, double? footOffset = null)
    {
        this.area = area; windowWidth = width;
        this.footOffset = footOffset ?? height;
        floorY = area.Bottom - this.footOffset;
        hasEnvironment = true;
        if (Paused || State == SmileyState.Dragging) return;
        ReconcilePlatforms();
        X = Math.Clamp(X, area.Left, Math.Max(area.Left, area.Right - width));
        if (ActiveSupport is { } support)
        {
            if (!FitsArea(support.Segment, area) || !HasFootContact(X, support.Segment)) DropSupport();
            else { Y = support.SurfaceY - this.footOffset; UpdateRelativePosition(); }
        }
        if (ActiveSupport is null && (IsGrounded || Y > floorY || (Y >= floorY - 1e-9 && VelocityY >= 0)))
        { Y = floorY; VelocityY = 0; IsGrounded = true; }
    }

    public void MoveTo(double x, double y, WorkArea area, double width, double height, double? footOffset = null)
    {
        ActiveSupport = null; supportMap = null;
        UpdateEnvironment(area, width, height, footOffset);
        X = Math.Clamp(x, area.Left, Math.Max(area.Left, area.Right - width));
        Y = Math.Clamp(y, Math.Min(area.Top, floorY), floorY);
        VelocityX = VelocityY = 0;
        IsGrounded = Y >= floorY - 1e-9;
    }

    public void SetPaused(bool paused) => Paused = paused;
    // The desktop attaches one decision layer. The legacy standalone schedule remains
    // available to existing controller clients/tests; attached controllers never run both.
    internal void UseExternalBehavior() { externalBehavior = true; Rest(); }
    public bool TryRest(Pose pose = Pose.Neutral)
    {
        if (Paused || !IsGrounded || State is SmileyState.Dragging or SmileyState.Reacting) return false;
        if (State != SmileyState.Idle || Pose != pose) { Rest(); Pose = pose; }
        return true;
    }
    public bool TryMove(MovementMode mode, int direction, double duration)
    {
        if (Paused || !IsGrounded || State is SmileyState.Dragging or SmileyState.Reacting ||
            !Enum.IsDefined(mode) || direction is not (-1 or 1) || !double.IsFinite(duration) || duration <= 0) return false;
        MovementMode = mode; Direction = direction;
        State = mode == MovementMode.Running ? SmileyState.Running : SmileyState.Walking;
        Pose = Pose.Running; animationSeconds = 0; remaining = duration;
        VelocityX = direction * (mode == MovementMode.Running ? options.RunSpeed : options.Speed) * scaleX;
        return true;
    }
    public void SetMovementMode(MovementMode mode)
    {
        if (!Enum.IsDefined(mode)) throw new ArgumentOutOfRangeException(nameof(mode));
        if (MovementMode == mode) return;
        MovementMode = mode;
        if (HasLocomotion)
        {
            State = mode == MovementMode.Running ? SmileyState.Running : SmileyState.Walking;
            animationSeconds = 0;
        }
    }
    public void BeginDrag()
    {
        ActiveSupport = null; supportMap = null;
        IsGrounded = hasEnvironment && Y >= floorY - 1e-9;
        State = SmileyState.Dragging; Pose = Pose.Startled; animationSeconds = 0; VelocityX = VelocityY = 0;
    }
    public void EndDrag(double velocityX = 0, double velocityY = 0, double dpiScaleX = 1, double dpiScaleY = 1)
    {
        VelocityX = VelocityY = 0; Rest();
        if (!double.IsFinite(velocityX) || !double.IsFinite(velocityY) ||
            !double.IsFinite(dpiScaleX) || dpiScaleX <= 0 || !double.IsFinite(dpiScaleY) || dpiScaleY <= 0) { TryReleaseContact(); return; }
        double vx = velocityX / dpiScaleX, vy = velocityY / dpiScaleY;
        double speed = Math.Sqrt(vx * vx + vy * vy);
        scaleX = dpiScaleX; scaleY = dpiScaleY;
        if (!double.IsFinite(speed) || speed < options.ThrowMinimumSpeed || speed == 0) { TryReleaseContact(); return; }
        double multiplier = Math.Min(1, options.ThrowMaximumSpeed / speed);
        if (vy >= 0) TryReleaseContact();
        if (IsGrounded && vy >= 0) return; // Contact with the ground absorbs the impulse.
        VelocityX = vx * multiplier * scaleX; VelocityY = vy * multiplier * scaleY;
        ActiveSupport = null; supportMap = null;
        IsGrounded = false;
        if (Math.Abs(VelocityX) > 1) Direction = Math.Sign(VelocityX);
    }
    public bool TryJump(double dpiScaleY = 1)
    {
        if (!CanJump || !double.IsFinite(dpiScaleY) || dpiScaleY <= 0) return false;
        scaleY = dpiScaleY;
        ActiveSupport = null; supportMap = null;
        VelocityY = -options.JumpSpeed * scaleY;
        VelocityX = 0; IsGrounded = false; animationSeconds = 0;
        return true;
    }
    // Autonomous launch only. Manual TryJump remains vertical; integration/collisions are shared.
    public bool TryDirectedJump(double horizontalVelocity, double dpiScaleX = 1, double dpiScaleY = 1)
    {
        if (!double.IsFinite(horizontalVelocity) || !double.IsFinite(dpiScaleX) || dpiScaleX <= 0 ||
            Math.Abs(horizontalVelocity) > options.RunSpeed * dpiScaleX + 1e-9 || !TryJump(dpiScaleY)) return false;
        scaleX = dpiScaleX; VelocityX = horizontalVelocity;
        if (Math.Abs(horizontalVelocity) > 1e-9) Direction = Math.Sign(horizontalVelocity);
        return true;
    }
    public void React()
    {
        State = SmileyState.Reacting;
        animationSeconds = 0;
        Pose = random.Next(2) == 0 ? Pose.Startled : Pose.Frowning;
        remaining = options.ReactionSeconds;
    }

    public void SetPlatforms(WindowPlatformMap map) => platforms = map;

    public void ReconcilePlatforms(Func<double, double, WorkArea>? areaAtPoint = null)
    {
        if (Paused || State == SmileyState.Dragging || ActiveSupport is not { } support || ReferenceEquals(supportMap, platforms)) return;
        if (platforms.IsAvailable)
        {
            bool replaced = platforms.Source.Version != support.Segment.SnapshotVersion &&
                platforms.Source.Changes.Any(c => c.Handle == support.Handle && c.Kind.HasFlag(WindowChangeKind.Disappeared));
            if (!replaced)
            foreach (var segment in platforms.Platforms)
            {
                if (segment.Handle != support.Handle) continue;
                var source = platforms.GetSource(segment).Bounds;
                double x = source.Left + support.RelativeX;
                var target = areaAtPoint?.Invoke(x + windowWidth / 2, segment.Y - footOffset / 2) ?? area;
                x = Math.Clamp(x, target.Left, Math.Max(target.Left, target.Right - windowWidth));
                if (!FitsArea(segment, target) || !HasFootContact(x, segment)) continue;
                X = x; Y = segment.Y - footOffset; VelocityY = 0; IsGrounded = true;
                ActiveSupport = new(segment, source, x - source.Left); supportMap = platforms;
                return;
            }
        }
        DropSupport(); // No valid replacement: never move to a guessed surface/location.
    }

    private bool FitsArea(WindowPlatform segment, WorkArea target)
        => segment.IsAvailable && segment.Y >= target.Top + footOffset - 1e-9 && segment.Y <= target.Bottom + 1e-9;
    private bool HasFootContact(double x, WindowPlatform segment)
    {
        double center = x + windowWidth / 2, half = options.FootSupportWidth * scaleX / 2;
        return Math.Min(center + half, segment.Right) - Math.Max(center - half, segment.Left) >= options.MinimumFootOverlap * scaleX - 1e-9;
    }
    private void DropSupport() { ActiveSupport = null; supportMap = null; IsGrounded = false; VelocityY = 0; }
    private void UpdateRelativePosition()
    {
        if (ActiveSupport is { } support) ActiveSupport = support with { RelativeX = X - support.SourceBounds.Left };
    }
    private void Attach(WindowPlatform segment, bool resetAnimation = true)
    {
        var source = platforms.GetSource(segment).Bounds;
        ActiveSupport = new(segment, source, X - source.Left); supportMap = platforms;
        Y = segment.Y - footOffset; IsGrounded = true; VelocityY = 0;
        if (resetAnimation) animationSeconds = 0;
    }
    private void TryReleaseContact()
    {
        if (!platforms.IsAvailable || IsGrounded) return;
        foreach (var segment in platforms.Platforms)
            if (FitsArea(segment, area) && Math.Abs(FeetY - segment.Y) <= options.ReleaseContactTolerance * scaleY && HasFootContact(X, segment))
            { Attach(segment); return; }
    }
    private double DownwardCrossing(double bodySurfaceY, double gravity)
    {
        double distance = bodySurfaceY - Y;
        double discriminant = VelocityY * VelocityY + 2 * gravity * distance;
        if (discriminant < 0) return double.PositiveInfinity;
        double root = Math.Sqrt(discriminant);
        double time = VelocityY >= 0 ? 2 * distance / (root + VelocityY) : (root - VelocityY) / gravity;
        return double.IsNaN(time) ? 0 : time >= 0 ? time : double.PositiveInfinity;
    }

    public void Tick(double seconds, WorkArea area, double width, double height, double dpiScale = 1, double? dpiScaleY = null, double? footOffset = null)
    {
        double newScaleY = dpiScaleY ?? dpiScale;
        if (!double.IsFinite(seconds) || seconds <= 0 || !double.IsFinite(dpiScale) || dpiScale <= 0 || !double.IsFinite(newScaleY) || newScaleY <= 0) return;
        if (State == SmileyState.Dragging || Paused) return;
        VelocityX *= dpiScale / scaleX;
        VelocityY *= newScaleY / scaleY;
        scaleX = dpiScale; scaleY = newScaleY;
        UpdateEnvironment(area, width, height, footOffset);
        double gravity = options.Gravity * scaleY;
        // Exact integration of constant acceleration. Split at floor, wall and behaviour
        // events, so even a long update lands once and consumes the remaining time correctly.
        while (seconds > 1e-9)
        {
            bool onGround = IsGrounded;
            bool advanceBehaviour = (onGround && (!externalBehavior || State != SmileyState.Idle)) || State == SmileyState.Reacting;
            double step = advanceBehaviour ? Math.Min(seconds, remaining) : seconds;
            double landingTime = double.PositiveInfinity, wallTime = double.PositiveInfinity, supportEdgeTime = double.PositiveInfinity;
            WindowPlatform? landingPlatform = null;
            if (onGround) VelocityX = HasLocomotion ? Direction * (State == SmileyState.Running ? options.RunSpeed : options.Speed) * scaleX : 0;
            if (VelocityX != 0)
            {
                double edge = VelocityX > 0 ? Math.Max(this.area.Left, this.area.Right - windowWidth) : this.area.Left;
                wallTime = Math.Max(0, (edge - X) / VelocityX);
                step = Math.Min(step, wallTime);
                if (onGround && ActiveSupport is { } support)
                {
                    double half = options.FootSupportWidth * scaleX / 2, minimum = options.MinimumFootOverlap * scaleX;
                    double footEdge = VelocityX > 0 ? support.Segment.Right + half - minimum : support.Segment.Left - half + minimum;
                    supportEdgeTime = Math.Max(0, (footEdge - windowWidth / 2 - X) / VelocityX);
                    step = Math.Min(step, supportEdgeTime);
                }
            }
            if (!onGround)
            {
                landingTime = DownwardCrossing(floorY, gravity);
                if (platforms.IsAvailable)
                foreach (var segment in platforms.Platforms)
                {
                    if (!FitsArea(segment, this.area)) continue;
                    double time = DownwardCrossing(segment.Y - this.footOffset, gravity);
                    // Zero-time capture is reserved for a valid manual release. This
                    // prevents a newly appearing window/old edge from grabbing the body.
                    if (time <= 1e-9 || time > step + 1e-9 || !HasFootContact(X + VelocityX * time, segment)) continue;
                    if (time < landingTime - 1e-9 || (landingPlatform is { } old && Math.Abs(time - landingTime) <= 1e-9 &&
                        platforms.GetSource(segment).ZOrder < platforms.GetSource(old).ZOrder))
                    { landingTime = time; landingPlatform = segment; }
                }
                step = Math.Min(step, landingTime);
            }
            if (VelocityX != 0)
            {
                X += VelocityX * step;
                if (onGround && HasLocomotion) animationSeconds += step;
            }
            if (!onGround)
            { Y += VelocityY * step + 0.5 * gravity * step * step; VelocityY += gravity * step; }
            if (advanceBehaviour) remaining -= step;
            seconds -= step;
            if (landingTime <= step + 1e-9)
            {
                if (landingPlatform is { } platform) Attach(platform);
                else { ActiveSupport = null; supportMap = null; Y = floorY; VelocityY = 0; IsGrounded = true; animationSeconds = 0; }
                VelocityX = 0;
            }
            if (onGround && ActiveSupport is not null) UpdateRelativePosition();
            if (supportEdgeTime <= step + 1e-9 && ActiveSupport is { } outgoing)
            {
                bool transferred = false;
                foreach (var adjacent in platforms.Platforms)
                {
                    if (adjacent.Y != outgoing.SurfaceY || adjacent == outgoing.Segment || !FitsArea(adjacent, this.area)) continue;
                    if (!HasFootContact(X, adjacent) || !HasFootContact(X + VelocityX * 1e-6, adjacent)) continue;
                    Attach(adjacent, resetAnimation: false); transferred = true; break;
                }
                if (!transferred) DropSupport();
            }
            if (wallTime <= step + 1e-9)
            {
                X = Math.Clamp(X, this.area.Left, Math.Max(this.area.Left, this.area.Right - windowWidth));
                if (onGround) Rest(); else VelocityX = 0;
                VelocityX = 0;
                continue;
            }
            if (!advanceBehaviour || remaining > 1e-9) continue;
            if (State == SmileyState.Idle)
            {
                State = MovementMode == MovementMode.Running ? SmileyState.Running : SmileyState.Walking;
                Pose = Pose.Running;
                animationSeconds = 0;
                Direction = random.Next(2) == 0 ? -1 : 1;
                remaining = Between(options.WalkMin, options.WalkMax);
                VelocityX = Direction * (State == SmileyState.Running ? options.RunSpeed : options.Speed) * scaleX;
            }
            else Rest();
        }
    }
}
