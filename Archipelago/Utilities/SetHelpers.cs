using System.Collections.Generic;
using System.Linq;

namespace ReTFO.Archipelago.Utilities;

public static class SetHelpers
{

    public static bool TryIntersect<T>(this IReadOnlySet<T> self, IEnumerable<T> other, out IEnumerable<T> intersection)
    {
        static IEnumerable<T> intersectHelper(IReadOnlySet<T> self, IEnumerable<T> other)
        {
            foreach (var element in other)
                if (self.Contains(element)) yield return element;
        }

        intersection = intersectHelper(self, other);
        return intersection.Any();
    }

}
