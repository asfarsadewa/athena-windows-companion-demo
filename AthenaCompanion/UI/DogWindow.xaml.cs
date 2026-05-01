using AthenaCompanion.UI.Interop;
using System.Windows;
using System.Windows.Media;

namespace AthenaCompanion.UI;

internal partial class DogWindow : Window
{
    private readonly SpriteAtlas _atlas = SpriteAtlas.Load("puppy-atlas.json", "puppy-atlas.png");

    public DogWindow()
    {
        InitializeComponent();
    }

    public void Render(DogCompanionSnapshot snapshot, double now)
    {
        Left = snapshot.X;
        Top = snapshot.Top;

        var clip = snapshot.Mode switch
        {
            DogBehaviorMode.Bark when _atlas.BarkClip is not null => _atlas.BarkClip,
            DogBehaviorMode.Idle => _atlas.PoseClip,
            _ => _atlas.WalkClip
        };

        DogSpriteImage.Source = _atlas.GetFrame(clip, now - snapshot.ModeStartedSeconds);
        DogSpriteImage.RenderTransform = snapshot.Direction < 0
            ? new ScaleTransform(-1, 1)
            : Transform.Identity;

        if (string.IsNullOrWhiteSpace(snapshot.BarkText))
        {
            BarkBubble.Visibility = Visibility.Collapsed;
            return;
        }

        BarkBubbleText.Text = snapshot.BarkText;
        BarkBubble.Visibility = Visibility.Visible;
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        ClickThroughInterop.Apply(this, enabled: true);
    }
}
