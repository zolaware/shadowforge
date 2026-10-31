namespace ShadowForge.GameData.Mods.Merge;

/// <summary>
/// One run where two sequences differ: base items BaseStart..BaseEnd became other items
/// OtherStart..OtherEnd. An empty base range is an insertion, an empty other range a deletion.
/// </summary>
internal readonly record struct DiffHunk(int BaseStart, int BaseEnd, int OtherStart, int OtherEnd)
{
    public bool IsInsertion => BaseStart == BaseEnd;
}

/// <summary>
/// The shortest edit script between two sequences, found with Myers' algorithm after the
/// common head and tail are set aside.
/// </summary>
internal static class SequenceDiff
{
    public static List<DiffHunk> Compute(IReadOnlyList<string> baseItems, IReadOnlyList<string> otherItems)
    {
        int shorter = Math.Min(baseItems.Count, otherItems.Count);
        int head = 0;
        while (head < shorter && baseItems[head] == otherItems[head]) head++;

        int tail = 0;
        while (tail < shorter - head
               && baseItems[baseItems.Count - 1 - tail] == otherItems[otherItems.Count - 1 - tail])
            tail++;

        int baseEnd = baseItems.Count - tail;
        int otherEnd = otherItems.Count - tail;
        var matches = MatchedPairs(baseItems, otherItems, head, baseEnd, head, otherEnd);

        var hunks = new List<DiffHunk>();
        int baseAt = head, otherAt = head;
        foreach (var (baseIndex, otherIndex) in matches)
        {
            if (baseIndex > baseAt || otherIndex > otherAt)
                hunks.Add(new DiffHunk(baseAt, baseIndex, otherAt, otherIndex));
            baseAt = baseIndex + 1;
            otherAt = otherIndex + 1;
        }
        if (baseAt < baseEnd || otherAt < otherEnd)
            hunks.Add(new DiffHunk(baseAt, baseEnd, otherAt, otherEnd));
        return hunks;
    }

    /// <summary>
    /// The items both ranges keep, as (base index, other index) pairs in ascending order. Each
    /// edit distance d keeps only the 2d + 1 diagonals it reached, so memory grows with the
    /// square of the number of edits rather than with the file length.
    /// </summary>
    private static List<(int BaseIndex, int OtherIndex)> MatchedPairs(
        IReadOnlyList<string> a, IReadOnlyList<string> b, int aStart, int aEnd, int bStart, int bEnd)
    {
        int n = aEnd - aStart, m = bEnd - bStart;
        var matches = new List<(int, int)>();
        if (n == 0 || m == 0) return matches;

        int max = n + m;
        int offset = max + 1;
        var furthest = new int[2 * max + 3];
        var trace = new List<int[]>();
        int found = -1;

        for (int d = 0; d <= max && found < 0; d++)
        {
            for (int k = -d; k <= d; k += 2)
            {
                int x = k == -d || (k != d && furthest[offset + k - 1] < furthest[offset + k + 1])
                    ? furthest[offset + k + 1]
                    : furthest[offset + k - 1] + 1;
                int y = x - k;
                while (x < n && y < m && a[aStart + x] == b[bStart + y]) { x++; y++; }
                furthest[offset + k] = x;
                if (x >= n && y >= m) found = d;
            }
            trace.Add(furthest[(offset - d)..(offset + d + 1)]);
        }

        int px = n, py = m;
        for (int d = found; d > 0; d--)
        {
            int[] previous = trace[d - 1];
            int k = px - py;
            int previousK = k == -d || (k != d && previous[k - 1 + d - 1] < previous[k + 1 + d - 1])
                ? k + 1
                : k - 1;
            int previousX = previous[previousK + d - 1];
            int previousY = previousX - previousK;
            while (px > previousX && py > previousY)
            {
                px--; py--;
                matches.Add((aStart + px, bStart + py));
            }
            px = previousX;
            py = previousY;
        }
        while (px > 0 && py > 0)
        {
            px--; py--;
            matches.Add((aStart + px, bStart + py));
        }

        matches.Reverse();
        return matches;
    }
}
