namespace AthenaCompanion.Music;

internal sealed record MusicPlayerRequest(string Query, bool Autoplay)
{
    public static MusicPlayerRequest OpenLibrary { get; } = new(string.Empty, Autoplay: false);
}
