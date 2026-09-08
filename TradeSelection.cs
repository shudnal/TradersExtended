using System;
using System.Collections.Generic;

namespace TradersExtended
{
    internal static class TradeSelection
    {
        // Prefer the same row when duplicate offers exist, then look for the same offer elsewhere.
        // An exhausted sale falls back to its nearest remaining neighbour, never to the buy list.
        internal static int RestoreIndex<T>(IList<T> items, T previous, int previousIndex, Func<T, T, bool> sameOffer)
        {
            if (previousIndex < 0 || items.Count == 0)
                return -1;
            if (previousIndex < items.Count && sameOffer(items[previousIndex], previous))
                return previousIndex;
            for (int index = 0; index < items.Count; index++)
                if (sameOffer(items[index], previous))
                    return index;
            return Math.Min(previousIndex, items.Count - 1);
        }
    }
}
