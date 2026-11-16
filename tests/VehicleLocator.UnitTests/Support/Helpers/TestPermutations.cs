namespace VehicleLocator.UnitTests;

internal static class TestPermutations
{
    public static IEnumerable<List<T>> Of<T>(List<T> items)
    {
        if (items.Count <= 1)
        {
            yield return [.. items];
            yield break;
        }

        for (var i = 0; i < items.Count; i++)
        {
            var rest = items.Where((_, idx) => idx != i).ToList();
            foreach (var p in Of(rest))
            {
                p.Insert(0, items[i]);
                yield return p;
            }
        }
    }
}
