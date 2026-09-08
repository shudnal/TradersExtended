using System;

namespace TradersExtended
{
    /// <summary>Whole trade-lot arithmetic, independent of the UI and inventory implementation.</summary>
    internal static class TradeAmounts
    {
        // Unity's Slider stores floats; every integer up to this limit is represented exactly.
        internal const int MaximumSliderLots = 1 << 24;

        internal static bool TryGetItemCount(int lotSize, int lots, out int amount)
        {
            long total = (long)lotSize * lots;
            amount = 0;
            if (lotSize <= 0 || lots <= 0 || total > int.MaxValue)
                return false;
            amount = (int)total;
            return true;
        }

        internal static bool TryGetPrice(int pricePerLot, int lots, float factor, out int price, bool roundUp = true)
        {
            // Trader factors are rounded to whole percentage points before reaching this boundary.
            // Do not promote a binary float approximation (for example 0.99f) into a pricing error.
            return TryGetPrice(pricePerLot, lots, Math.Round((double)factor, 2), out price, roundUp);
        }

        internal static bool TryGetPrice(int pricePerLot, int lots, double factor, out int price, bool roundUp = true)
        {
            price = 0;
            if (pricePerLot <= 0 || lots <= 0 || double.IsNaN(factor) || double.IsInfinity(factor) || factor < 0d || factor > int.MaxValue)
                return false;

            // All three factors are bounded by Int32.MaxValue, so their product fits Decimal.
            decimal product = (decimal)pricePerLot * lots * (decimal)factor;
            decimal total = Math.Max(roundUp ? decimal.Ceiling(product) : decimal.Floor(product), 1m);
            if (total > int.MaxValue)
                return false;
            price = (int)total;
            return true;
        }

        internal static bool TryGetQualityPrice(int basePrice, int quality, float multiplier, out int price)
        {
            price = 0;
            if (basePrice <= 0 || quality <= 0 || float.IsNaN(multiplier) || float.IsInfinity(multiplier) ||
                Math.Abs((double)multiplier) > int.MaxValue)
                return false;
            decimal total = basePrice * (1m + (decimal)multiplier * ((long)quality - 1));
            if (total < 1m || total > int.MaxValue)
                return false;
            price = (int)total;
            return true;
        }

        internal static int MaximumBuyLots(int lotSize, int pricePerLot, int currency, int capacity)
        {
            if (lotSize <= 0 || pricePerLot <= 0 || currency <= 0 || capacity <= 0)
                return 0;
            return Math.Min(MaximumSliderLots, Math.Min(capacity / lotSize, currency / pricePerLot));
        }

        internal static int MaximumSellLots(int lotSize, int pricePerLot, int availableItems, float factor, int budget)
        {
            return MaximumSellLots(lotSize, pricePerLot, availableItems, Math.Round((double)factor, 2), budget);
        }

        internal static int MaximumSellLots(int lotSize, int pricePerLot, int availableItems, double factor, int budget)
        {
            if (lotSize <= 0 || pricePerLot <= 0 || availableItems <= 0 || budget <= 0)
                return 0;

            int low = 0;
            int high = Math.Min(MaximumSliderLots, availableItems / lotSize);
            while (low < high)
            {
                int middle = low + (high - low + 1) / 2;
                if (TryGetPrice(pricePerLot, middle, factor, out int price) && price <= budget)
                    low = middle;
                else
                    high = middle - 1;
            }
            return low;
        }

        internal static bool TryEncodeStack(int stack, int quality, int multiplier, out int encoded)
        {
            encoded = 0;
            long value = stack + (long)multiplier * quality;
            if (multiplier <= 1 || stack <= 0 || stack >= multiplier || quality < 0 || value > int.MaxValue)
                return false;
            encoded = (int)value;
            return true;
        }

        internal static int ClampBalance(long amount)
        {
            return (int)Math.Max(0L, Math.Min(int.MaxValue, amount));
        }
    }
}
