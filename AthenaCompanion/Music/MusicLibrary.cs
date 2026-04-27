using System.IO;
using System.Text;

namespace AthenaCompanion.Music;

internal sealed record MusicTrack(string FilePath, string DisplayName, string RelativePath);

internal sealed record MusicLibrarySnapshot(string DirectoryPath, IReadOnlyList<MusicTrack> Tracks)
{
    public bool IsEmpty => Tracks.Count == 0;

    public MusicTrack? FindBestMatch(string? query)
    {
        if (Tracks.Count == 0)
        {
            return null;
        }

        if (MusicQuery.IsGeneric(query))
        {
            return Tracks[0];
        }

        return MusicSearch.FindBestMatch(Tracks, query);
    }
}

internal static class MusicSearch
{
    private static readonly HashSet<string> QueryStopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "a",
        "an",
        "can",
        "could",
        "me",
        "music",
        "play",
        "please",
        "song",
        "songs",
        "some",
        "the",
        "track",
        "tracks",
        "you"
    };

    public static MusicTrack? FindBestMatch(IReadOnlyList<MusicTrack> tracks, string? query)
    {
        var normalizedQuery = NormalizeQuery(query);
        if (string.IsNullOrWhiteSpace(normalizedQuery))
        {
            return tracks.Count == 0 ? null : tracks[0];
        }

        var bestScore = 0;
        MusicTrack? bestTrack = null;
        foreach (var track in tracks)
        {
            var score = ScoreTrack(track, normalizedQuery);
            if (score > bestScore)
            {
                bestScore = score;
                bestTrack = track;
            }
        }

        return bestScore > 0 ? bestTrack : null;
    }

    internal static string NormalizeQuery(string? query)
    {
        var normalized = NormalizeText(query);
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        var tokens = Tokenize(normalized)
            .Where(token => !QueryStopWords.Contains(token))
            .ToArray();

        return string.Join(' ', tokens);
    }

    internal static int ScoreTrack(MusicTrack track, string normalizedQuery)
    {
        var candidates = new[]
        {
            track.DisplayName,
            RemoveExtension(track.RelativePath),
            Path.GetFileNameWithoutExtension(track.FilePath)
        };

        return candidates
            .Select(candidate => ScoreCandidate(NormalizeText(candidate), normalizedQuery))
            .Max();
    }

    private static int ScoreCandidate(string candidate, string query)
    {
        if (string.IsNullOrWhiteSpace(candidate) || string.IsNullOrWhiteSpace(query))
        {
            return 0;
        }

        var queryTokens = Tokenize(query).ToArray();
        var candidateTokens = Tokenize(candidate).ToArray();
        if (queryTokens.Length == 0 || candidateTokens.Length == 0)
        {
            return 0;
        }

        if (string.Equals(candidate, query, StringComparison.Ordinal))
        {
            return 5000 + query.Length;
        }

        if (ContainsNormalizedPhrase(candidate, query))
        {
            return 4000 + query.Length;
        }

        var compactCandidate = candidate.Replace(" ", string.Empty, StringComparison.Ordinal);
        var compactQuery = query.Replace(" ", string.Empty, StringComparison.Ordinal);
        if (compactCandidate.Contains(compactQuery, StringComparison.Ordinal))
        {
            return 3000 + compactQuery.Length;
        }

        if (ContainsTokensInOrder(candidateTokens, queryTokens))
        {
            return 2000 + queryTokens.Length;
        }

        if (queryTokens.All(queryToken => candidateTokens.Contains(queryToken, StringComparer.Ordinal)))
        {
            return 1500 + queryTokens.Length;
        }

        if (queryTokens.All(queryToken => HasConservativeTokenMatch(candidateTokens, queryToken)))
        {
            return 800 + queryTokens.Length;
        }

        return 0;
    }

    private static bool ContainsNormalizedPhrase(string candidate, string query) =>
        string.Equals(candidate, query, StringComparison.Ordinal) ||
        candidate.StartsWith(query + " ", StringComparison.Ordinal) ||
        candidate.EndsWith(" " + query, StringComparison.Ordinal) ||
        candidate.Contains(" " + query + " ", StringComparison.Ordinal);

    private static bool ContainsTokensInOrder(IReadOnlyList<string> candidateTokens, IReadOnlyList<string> queryTokens)
    {
        var candidateIndex = 0;
        foreach (var queryToken in queryTokens)
        {
            var found = false;
            while (candidateIndex < candidateTokens.Count)
            {
                if (string.Equals(candidateTokens[candidateIndex], queryToken, StringComparison.Ordinal))
                {
                    found = true;
                    candidateIndex++;
                    break;
                }

                candidateIndex++;
            }

            if (!found)
            {
                return false;
            }
        }

        return true;
    }

    private static bool HasConservativeTokenMatch(IEnumerable<string> candidateTokens, string queryToken) =>
        candidateTokens.Any(candidateToken =>
            string.Equals(candidateToken, queryToken, StringComparison.Ordinal) ||
            (queryToken.Length >= 5 &&
                candidateToken.Length >= 5 &&
                Math.Abs(candidateToken.Length - queryToken.Length) <= 1 &&
                LevenshteinDistanceAtMostOne(candidateToken, queryToken)));

    private static bool LevenshteinDistanceAtMostOne(string left, string right)
    {
        if (string.Equals(left, right, StringComparison.Ordinal))
        {
            return true;
        }

        if (Math.Abs(left.Length - right.Length) > 1)
        {
            return false;
        }

        var edits = 0;
        var leftIndex = 0;
        var rightIndex = 0;
        while (leftIndex < left.Length && rightIndex < right.Length)
        {
            if (left[leftIndex] == right[rightIndex])
            {
                leftIndex++;
                rightIndex++;
                continue;
            }

            edits++;
            if (edits > 1)
            {
                return false;
            }

            if (left.Length == right.Length)
            {
                leftIndex++;
                rightIndex++;
            }
            else if (left.Length > right.Length)
            {
                leftIndex++;
            }
            else
            {
                rightIndex++;
            }
        }

        if (leftIndex < left.Length || rightIndex < right.Length)
        {
            edits++;
        }

        return edits <= 1;
    }

    private static string RemoveExtension(string value)
    {
        try
        {
            return Path.ChangeExtension(value, null) ?? value;
        }
        catch
        {
            return value;
        }
    }

    private static string NormalizeText(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        var builder = new StringBuilder(value.Length);
        var previousWasSpace = true;
        foreach (var character in value)
        {
            if (char.IsLetterOrDigit(character))
            {
                builder.Append(char.ToLowerInvariant(character));
                previousWasSpace = false;
            }
            else if (!previousWasSpace)
            {
                builder.Append(' ');
                previousWasSpace = true;
            }
        }

        return builder.ToString().Trim();
    }

    private static IEnumerable<string> Tokenize(string value) =>
        value.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
}

internal static class MusicLibrary
{
    private static readonly HashSet<string> SupportedExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp3",
        ".m4a"
    };

    public static MusicLibrarySnapshot Load(string directoryPath)
    {
        Directory.CreateDirectory(directoryPath);

        var tracks = EnumerateFilesSafely(directoryPath)
            .Where(path => SupportedExtensions.Contains(Path.GetExtension(path)))
            .Select(path => new MusicTrack(
                path,
                Path.GetFileNameWithoutExtension(path),
                Path.GetRelativePath(directoryPath, path)))
            .OrderBy(track => track.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        return new MusicLibrarySnapshot(directoryPath, tracks);
    }

    private static IEnumerable<string> EnumerateFilesSafely(string root)
    {
        var pending = new Stack<string>();
        pending.Push(root);

        while (pending.Count > 0)
        {
            var directory = pending.Pop();
            string[] files;
            try
            {
                files = Directory.GetFiles(directory);
            }
            catch
            {
                continue;
            }

            foreach (var file in files)
            {
                yield return file;
            }

            string[] directories;
            try
            {
                directories = Directory.GetDirectories(directory);
            }
            catch
            {
                continue;
            }

            foreach (var child in directories)
            {
                pending.Push(child);
            }
        }
    }
}

internal static class MusicLibraryMessages
{
    public static string Empty(string directoryPath) =>
        $"Add MP3 or M4A files to {directoryPath}.";

    public static string NoMatch(string query) =>
        string.IsNullOrWhiteSpace(query)
            ? "No music found."
            : $"No match found for \"{query.Trim()}\".";
}

internal static class MusicQuery
{
    private static readonly HashSet<string> GenericQueries = new(StringComparer.OrdinalIgnoreCase)
    {
        "music",
        "play music",
        "song",
        "songs",
        "play a song",
        "play something",
        "anything",
        "shuffle"
    };

    public static bool IsGeneric(string? query) =>
        string.IsNullOrWhiteSpace(query) || GenericQueries.Contains(query.Trim());
}
