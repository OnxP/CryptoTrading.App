using System;

namespace CryptoTrading.App.Core.Exchange
{
    /// <summary>
    /// Exchange-agnostic live-stream candle event. Replaces the bundled
    /// <c>Binance.Client.CandlestickEventArgs</c> on the market-data /
    /// monitor interfaces.
    ///
    /// The wrapper preserves the <c>args.Candlestick.*</c> accessor shape
    /// that ~88 existing call-sites use, and exposes <see cref="IsFinal"/>
    /// as an alias for <see cref="ExchangeCandlestick.IsClosed"/> so the
    /// very common <c>args.IsFinal</c> check keeps working unchanged.
    /// </summary>
    public sealed class ExchangeCandlestickEvent
    {
        /// <summary>
        /// The candle payload. Intra-bar updates carry the provisional
        /// values; on bar close <see cref="IsFinal"/> becomes true and the
        /// candle's final OHLCV is reflected here.
        /// </summary>
        public ExchangeCandlestick Candlestick { get; }

        /// <summary>
        /// When the exchange emitted the event (distinct from the candle's
        /// own OpenTime/CloseTime). REST-sourced synthetic events populate
        /// this with <see cref="DateTime.UtcNow"/> or the candle CloseTime.
        /// </summary>
        public DateTime EventTime { get; }

        /// <summary>
        /// Alias for <see cref="ExchangeCandlestick.IsClosed"/>. True when
        /// this event represents a closed bar (as opposed to an intra-bar
        /// websocket tick).
        /// </summary>
        public bool IsFinal => Candlestick != null && Candlestick.IsClosed;

        public ExchangeCandlestickEvent(ExchangeCandlestick candlestick, DateTime eventTime)
        {
            Candlestick = candlestick ?? throw new ArgumentNullException(nameof(candlestick));
            EventTime = eventTime;
        }
    }
}
