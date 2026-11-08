namespace RokMahjong.Core;

public static class Pairing
{
    /// <summary>
    /// Builds matching pairs from a multiset of faces. Regular faces pair with
    /// themselves; flower and season faces pair with any face in the same group,
    /// so four distinct flower arts become two pairs.
    /// </summary>
    public static List<(TileFace First, TileFace Second)>? BuildPairUnits(IEnumerable<TileFace> faces)
    {
        var counts = new Dictionary<TileFace, int>();
        foreach (var face in faces)
            counts[face] = counts.TryGetValue(face, out var count) ? count + 1 : 1;

        var pairs = new List<(TileFace First, TileFace Second)>();
        var singles = new List<TileFace>();
        foreach (var (face, count) in counts)
        {
            for (var i = 0; i < count / 2; i++)
                pairs.Add((face, face));
            if (count % 2 != 0) singles.Add(face);
        }

        foreach (var group in singles.GroupBy(face => face.MatchKey))
        {
            var groupFaces = group.ToList();
            if (groupFaces.Count % 2 != 0) return null;
            for (var i = 0; i < groupFaces.Count; i += 2)
                pairs.Add((groupFaces[i], groupFaces[i + 1]));
        }

        return pairs;
    }
}
