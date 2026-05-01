namespace AthenaCompanion.UI;

internal enum DogBehaviorMode
{
    Roam,
    Idle,
    Bark
}

internal readonly record struct DogCompanionFrame(
    double AthenaLeft,
    double AthenaTop,
    double AthenaWidth,
    double AthenaHeight,
    double WorkAreaLeft,
    double WorkAreaRight,
    double DogWidth,
    double DogHeight)
{
    public double AthenaCenterX => AthenaLeft + AthenaWidth / 2;
    public double DogTop => AthenaTop + AthenaHeight - DogHeight + 5;
}

internal readonly record struct DogCompanionSnapshot(
    double X,
    double Top,
    int Direction,
    DogBehaviorMode Mode,
    double ModeStartedSeconds,
    string? BarkText);

internal sealed class DogCompanionController
{
    private const double EdgePadding = 6;
    private const double MaxCenterDistanceFromAthena = 142;
    private const double RoamCloseEnough = 5;
    private const double MinRoamSpeed = 48;
    private const double MaxRoamSpeed = 78;
    private const double CatchUpSpeed = 108;

    private static readonly string[] BarkVariants = ["woof", "arf", "yip", "ruff"];

    private readonly Random _random;

    private bool _initialized;
    private double _x;
    private double _targetX;
    private double _modeStartedSeconds;
    private double _nextDecisionSeconds;
    private double _nextBarkSeconds;
    private double _barkDurationSeconds;
    private int _direction = 1;
    private DogBehaviorMode _mode = DogBehaviorMode.Idle;
    private string? _barkText;

    public DogCompanionController(Random? random = null)
    {
        _random = random ?? new Random();
    }

    public DogCompanionSnapshot Tick(double now, double dt, DogCompanionFrame frame)
    {
        dt = Math.Clamp(dt, 0, 0.1);
        var range = GetAllowedLeftRange(frame);

        if (!_initialized)
        {
            Initialize(now, frame, range);
        }

        if (_mode != DogBehaviorMode.Bark && now >= _nextBarkSeconds)
        {
            EnterBark(now);
        }

        switch (_mode)
        {
            case DogBehaviorMode.Bark:
                TickBark(now);
                break;
            case DogBehaviorMode.Idle:
                TickIdle(now, frame, range);
                break;
            default:
                TickRoam(now, dt, frame, range);
                break;
        }

        _x = Math.Clamp(_x, range.Min, range.Max);
        return new DogCompanionSnapshot(_x, frame.DogTop, _direction, _mode, _modeStartedSeconds, _barkText);
    }

    private void Initialize(double now, DogCompanionFrame frame, (double Min, double Max) range)
    {
        _initialized = true;
        _x = Math.Clamp(frame.AthenaCenterX - frame.DogWidth / 2 + RandomRange(-70, 70), range.Min, range.Max);
        _targetX = _x;
        _modeStartedSeconds = now;
        _nextDecisionSeconds = now + RandomRange(0.6, 1.4);
        _nextBarkSeconds = now + RandomRange(2.2, 5.8);
    }

    private void TickBark(double now)
    {
        if (now - _modeStartedSeconds < _barkDurationSeconds)
        {
            return;
        }

        EnterIdle(now);
    }

    private void TickIdle(double now, DogCompanionFrame frame, (double Min, double Max) range)
    {
        if (IsOutsideLeash(frame) || now >= _nextDecisionSeconds)
        {
            EnterRoam(now, frame, range);
        }
    }

    private void TickRoam(double now, double dt, DogCompanionFrame frame, (double Min, double Max) range)
    {
        if (IsOutsideLeash(frame))
        {
            _targetX = ChooseTargetX(frame, range, preferAthenaSide: true);
        }
        else
        {
            _targetX = Math.Clamp(_targetX, range.Min, range.Max);
        }

        var delta = _targetX - _x;
        if (Math.Abs(delta) <= RoamCloseEnough)
        {
            EnterIdle(now);
            return;
        }

        _direction = delta < 0 ? -1 : 1;
        var speed = IsOutsideLeash(frame) ? CatchUpSpeed : RandomRange(MinRoamSpeed, MaxRoamSpeed);
        var step = Math.Min(Math.Abs(delta), speed * dt);
        _x += Math.Sign(delta) * step;
    }

    private void EnterRoam(double now, DogCompanionFrame frame, (double Min, double Max) range)
    {
        _mode = DogBehaviorMode.Roam;
        _modeStartedSeconds = now;
        _barkText = null;
        _targetX = ChooseTargetX(frame, range, preferAthenaSide: false);

        var delta = _targetX - _x;
        if (Math.Abs(delta) > RoamCloseEnough)
        {
            _direction = delta < 0 ? -1 : 1;
        }
    }

    private void EnterIdle(double now)
    {
        _mode = DogBehaviorMode.Idle;
        _modeStartedSeconds = now;
        _barkText = null;
        _nextDecisionSeconds = now + RandomRange(0.7, 2.1);
    }

    private void EnterBark(double now)
    {
        _mode = DogBehaviorMode.Bark;
        _modeStartedSeconds = now;
        _barkDurationSeconds = RandomRange(1.05, 1.75);
        _barkText = BarkVariants[_random.Next(BarkVariants.Length)];
        _nextBarkSeconds = now + RandomRange(5.0, 10.0);
    }

    private double ChooseTargetX(DogCompanionFrame frame, (double Min, double Max) range, bool preferAthenaSide)
    {
        var offset = preferAthenaSide
            ? RandomRange(-42, 42)
            : RandomRange(-110, 110);
        return Math.Clamp(frame.AthenaCenterX - frame.DogWidth / 2 + offset, range.Min, range.Max);
    }

    private bool IsOutsideLeash(DogCompanionFrame frame)
    {
        var dogCenterX = _x + frame.DogWidth / 2;
        return Math.Abs(dogCenterX - frame.AthenaCenterX) > MaxCenterDistanceFromAthena;
    }

    private static (double Min, double Max) GetAllowedLeftRange(DogCompanionFrame frame)
    {
        var screenMin = frame.WorkAreaLeft + EdgePadding;
        var screenMax = frame.WorkAreaRight - frame.DogWidth - EdgePadding;
        if (screenMax < screenMin)
        {
            screenMax = screenMin;
        }

        var followMin = frame.AthenaCenterX - MaxCenterDistanceFromAthena - frame.DogWidth / 2;
        var followMax = frame.AthenaCenterX + MaxCenterDistanceFromAthena - frame.DogWidth / 2;
        var min = Math.Max(screenMin, followMin);
        var max = Math.Min(screenMax, followMax);

        if (max >= min)
        {
            return (min, max);
        }

        var fallback = Math.Clamp(frame.AthenaCenterX - frame.DogWidth / 2, screenMin, screenMax);
        return (fallback, fallback);
    }

    private double RandomRange(double min, double max) => min + _random.NextDouble() * (max - min);
}
