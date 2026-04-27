using System.Windows.Media;

namespace AthenaCompanion.UI;

internal sealed class WalkAnimationController
{
    private const double MinWalkSpeed = 34;
    private const double MaxWalkSpeed = 46;

    private readonly SpriteAtlas _atlas;
    private readonly Random _random;

    private BehaviorMode _mode = BehaviorMode.Walk;
    private double _modeStartedSeconds;
    private double _nextPoseSeconds;
    private double _poseDurationSeconds;
    private double _trackMinX;
    private double _trackMaxX;
    private double _walkSpeed;
    private double _x;
    private int _direction = 1;

    public WalkAnimationController(SpriteAtlas atlas, Random? random = null)
    {
        _atlas = atlas;
        _random = random ?? new Random();
        _walkSpeed = RandomRange(MinWalkSpeed, MaxWalkSpeed);
        _nextPoseSeconds = RandomRange(8, 18);
    }

    public double X => _x;
    public int Direction => _direction;
    public BehaviorMode Mode => _mode;
    public double ModeStartedSeconds => _modeStartedSeconds;

    public AnimationClip CurrentClip => _mode == BehaviorMode.Walk ? _atlas.WalkClip : _atlas.PoseClip;

    public ImageSource CurrentFrame(double now) =>
        _atlas.GetFrame(CurrentClip, now - _modeStartedSeconds);

    public void SetTrackBounds(double minX, double maxX, bool resetPosition)
    {
        _trackMinX = minX;
        _trackMaxX = maxX;

        if (resetPosition || _x < _trackMinX || _x > _trackMaxX)
        {
            _x = RandomRange(_trackMinX, _trackMaxX);
        }
    }

    public void Tick(double now, double dt, bool paused)
    {
        if (paused)
        {
            EnterPoseIfNeeded(now);
        }
        else
        {
            UpdateMovement(now, dt);
        }
    }

    public void EnterWalk(double now)
    {
        _mode = BehaviorMode.Walk;
        _modeStartedSeconds = now;
        _walkSpeed = RandomRange(MinWalkSpeed, MaxWalkSpeed);
        _nextPoseSeconds = now + RandomRange(8, 18);
    }

    public void EnterPose(double now, bool brief)
    {
        _mode = BehaviorMode.Pose;
        _modeStartedSeconds = now;
        _poseDurationSeconds = brief ? RandomRange(0.75, 1.35) : RandomRange(2.2, 4.8);
    }

    private void EnterPoseIfNeeded(double now)
    {
        if (_mode != BehaviorMode.Pose)
        {
            EnterPose(now, brief: false);
        }
    }

    private void UpdateMovement(double now, double dt)
    {
        if (_mode == BehaviorMode.Pose)
        {
            if (now - _modeStartedSeconds >= _poseDurationSeconds)
            {
                EnterWalk(now);
            }

            return;
        }

        _x += _direction * _walkSpeed * dt;

        if (_x <= _trackMinX)
        {
            _x = _trackMinX;
            _direction = 1;
            EnterPose(now, brief: true);
        }
        else if (_x >= _trackMaxX)
        {
            _x = _trackMaxX;
            _direction = -1;
            EnterPose(now, brief: true);
        }
        else if (now >= _nextPoseSeconds)
        {
            EnterPose(now, brief: false);
        }
    }

    private double RandomRange(double min, double max) => min + _random.NextDouble() * (max - min);
}
