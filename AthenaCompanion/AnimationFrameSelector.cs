namespace AthenaCompanion;

internal static class AnimationFrameSelector
{
    public static int SelectFrameIndex(AnimationClip clip, double clipSeconds, int frameTotal)
    {
        if (frameTotal <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(frameTotal), "Frame total must be positive.");
        }

        var count = Math.Min(clip.FrameCount, Math.Max(1, frameTotal - clip.StartFrame));
        var localFrame = (int)Math.Floor(Math.Max(0, clipSeconds) * clip.FramesPerSecond);

        if (clip.PingPong && count > 1)
        {
            var period = count * 2 - 2;
            localFrame %= period;
            if (localFrame >= count)
            {
                localFrame = period - localFrame;
            }
        }
        else
        {
            localFrame %= count;
        }

        return Math.Clamp(clip.StartFrame + localFrame, 0, frameTotal - 1);
    }
}
