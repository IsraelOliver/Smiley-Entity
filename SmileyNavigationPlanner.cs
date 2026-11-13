namespace Smiley;

public enum NavigationStage { Approaching, Flying }
public readonly record struct NavigationPlan(nint OriginHandle, WindowBounds OriginBounds, WindowPlatform? OriginSegment, WindowPlatform Target,
    double DepartureX, double LandingX, double OriginY, double FlightSeconds, double HorizontalVelocity);
public readonly record struct NavigationDiagnostic(string Status, nint Origin, nint Target, double? DepartureX = null,
    double? LandingX = null, double? TargetY = null);

// Pure, single-hop geometry planner. No timers, native calls, movement or random generator.
public sealed class SmileyNavigationPlanner
{
    public const double EdgeMarginDips = 8, HeightMarginDips = 8, HeadroomDips = 12;
    internal static bool SameSurface(WindowPlatform a, WindowPlatform b)
        => a.Handle == b.Handle && a.Left == b.Left && a.Right == b.Right && a.Y == b.Y;

    public bool TrySafeRange(SmileyController body, WorkArea area, double width, double sx, out double left, out double right)
    {
        left = area.Left + EdgeMarginDips * sx; right = area.Right - width - EdgeMarginDips * sx;
        if (body.ActiveSupport is { } support)
            ClipRange(support.Segment, body.MotionLimits, width, sx, ref left, ref right);
        return body.IsGrounded && left <= right;
    }

    public bool TryLocalDestination(SmileyController body, WorkArea area, double width, double sx, double fraction, out double x)
    {
        bool valid = TrySafeRange(body, area, width, sx, out var left, out var right);
        x = valid ? left + (right - left) * Math.Clamp(fraction, 0, 1) : body.X;
        return valid;
    }

    private static void ClipRange(WindowPlatform segment, SmileyMotionLimits limits, double width, double sx, ref double left, ref double right)
    {
        double inset = (limits.FootWidth / 2 + EdgeMarginDips) * sx;
        left = Math.Max(left, segment.Left + inset - width / 2);
        right = Math.Min(right, segment.Right - inset - width / 2);
    }

    public bool TryChoose(WindowPlatformMap map, bool fresh, SmileyController body, WorkArea area, double width,
        double sx, double sy, double maximumSpeedDips, Func<nint, bool>? blocked, out NavigationPlan plan, out string reason,
        nint targetHandle = 0)
    {
        plan = default; reason = "Nenhum destino alcançável.";
        if (!fresh || !map.IsAvailable) { reason = "Mapa sem coleta saudável recente."; return false; }
        if (!body.IsGrounded) { reason = "Personagem sem apoio."; return false; }
        if (!TrySafeRange(body, area, width, sx, out var sourceLeft, out var sourceRight))
        { reason = "Origem sem margem de apoio."; return false; }
        var limits = body.MotionLimits;
        double gravity = limits.Gravity * sy, jump = limits.JumpSpeed * sy;
        double feetOffset = body.FeetY - body.Y;
        if (body.Y - jump * jump / (2 * gravity) < area.Top + HeadroomDips * sy)
        { reason = "Sem espaço acima para o arco do salto."; return false; }
        double best = double.PositiveInfinity;
        foreach (var target in map.Platforms)
        {
            if (body.ActiveSupport is { } support && SameSurface(target, support.Segment) ||
                targetHandle != 0 && target.Handle != targetHandle || blocked?.Invoke(target.Handle) == true) continue;
            if (!target.IsAvailable || target.Y < area.Top + feetOffset || target.Y > area.Bottom) continue;
            double left = area.Left, right = area.Right - width;
            ClipRange(target, limits, width, sx, ref left, ref right);
            if (left > right) { reason = "Destino sem margem segura neste monitor."; continue; }
            double dy = target.Y - body.FeetY;
            if (-dy > jump * jump / (2 * gravity) - HeightMarginDips * sy)
            { reason = "Destino alto demais para o salto."; continue; }
            double discriminant = jump * jump + 2 * gravity * dy;
            if (discriminant <= 0) continue;
            double time = (jump + Math.Sqrt(discriminant)) / gravity; // Descending root, Y grows downward.
            double landing = Math.Clamp(body.X, left, right);
            double departure = Math.Clamp(landing, sourceLeft, sourceRight);
            double vx = (landing - departure) / time;
            if (Math.Abs(vx) > Math.Min(maximumSpeedDips, limits.RunSpeed) * sx + 1e-9)
            { reason = "Distância horizontal excede a velocidade disponível."; continue; }
            if (!ClearDescent(map, target, body.FeetY, departure, vx, time, jump, gravity, limits, width, sx))
            { reason = "Outra superfície intercepta a descida antes do destino."; continue; }
            double distance = Math.Abs(departure - body.X) + Math.Sqrt((landing - departure) * (landing - departure) + dy * dy);
            if (distance >= best) continue;
            best = distance;
            plan = new(body.ActiveSupport?.Handle ?? 0, body.ActiveSupport?.SourceBounds ?? default, body.ActiveSupport?.Segment, target,
                departure, landing, body.FeetY, time, vx);
        }
        if (!double.IsFinite(best)) return false;
        reason = "Salto alcançável."; return true;
    }

    private static bool ClearDescent(WindowPlatformMap map, WindowPlatform target, double originY, double departure,
        double vx, double duration, double jump, double gravity, SmileyMotionLimits limits, double width, double sx)
    {
        foreach (var obstacle in map.Platforms)
        {
            if (obstacle == target) continue;
            double discriminant = jump * jump + 2 * gravity * (obstacle.Y - originY);
            if (discriminant <= 0) continue;
            double time = (jump + Math.Sqrt(discriminant)) / gravity;
            if (time >= duration - 1e-9) continue;
            double center = departure + vx * time + width / 2, half = limits.FootWidth * sx / 2;
            if (Math.Min(center + half, obstacle.Right) - Math.Max(center - half, obstacle.Left) >= limits.MinimumFootOverlap * sx) return false;
        }
        return true;
    }
}
