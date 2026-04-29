using System;

namespace CryptoTrading.App.Algorithm.HtfRsiVolExpansion
{
    /// <summary>
    /// Pure function that turns (equity, leverage, price, exchange filters) into
    /// a position quantity, OR a skip reason. Pulled out of
    /// <see cref="HtfRsiVolExpansionAlgorithm"/> so the bankruptcy / equity-floor
    /// guard is testable in isolation.
    ///
    /// Sizing model: full equity × leverage / price.
    /// Guards: non-positive equity (no money to risk), non-positive leverage,
    /// non-positive price, exchange minimum quantity, exchange quantity step.
    ///
    /// The equity floor is the important one — without it, once equity goes to
    /// zero or negative the algorithm would still emit setups with negative or
    /// zero quantity. The minimum-quantity check would catch a negative qty as
    /// a side effect, but only because exchange minimums happen to be positive.
    /// Modelling bankruptcy as a first-class skip reason keeps the intent
    /// explicit.
    /// </summary>
    public static class HtfRsiPositionSizer
    {
        public readonly struct SizingResult
        {
            public bool Ok { get; }
            public decimal Quantity { get; }
            public string SkipReason { get; }

            private SizingResult(bool ok, decimal quantity, string skipReason)
            {
                Ok = ok;
                Quantity = quantity;
                SkipReason = skipReason;
            }

            public static SizingResult Skip(string reason) =>
                new SizingResult(false, 0m, reason);

            public static SizingResult Take(decimal quantity) =>
                new SizingResult(true, quantity, null);
        }

        public static SizingResult Compute(
            decimal equity,
            int leverage,
            decimal entryPrice,
            decimal minimumQuantity = 0m,
            decimal quantityIncrement = 0m)
        {
            if (equity <= 0m)
                return SizingResult.Skip(
                    $"Equity is non-positive ({equity:F2} USDT) - account is bankrupt; skipping setup.");

            if (leverage <= 0)
                return SizingResult.Skip($"Leverage must be positive (got {leverage}).");

            if (entryPrice <= 0m)
                return SizingResult.Skip($"Entry price must be positive (got {entryPrice}).");

            var notional = equity * leverage;
            var quantity = notional / entryPrice;

            if (minimumQuantity > 0m && quantity < minimumQuantity)
                return SizingResult.Skip(
                    $"Quantity {quantity:F6} below exchange minimum {minimumQuantity}.");

            if (quantityIncrement > 0m)
            {
                quantity = Math.Floor(quantity / quantityIncrement) * quantityIncrement;
                if (quantity <= 0m)
                    return SizingResult.Skip(
                        $"Quantity rounded down to zero by increment {quantityIncrement}.");
            }

            return SizingResult.Take(quantity);
        }
    }
}
