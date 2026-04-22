using Binance;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using System;

namespace CryptoTrading.App.Core.TradeRequest
{
    /// <summary>
    /// Backtest/simulated trade request. The public <see cref="ITradeRequest"/>
    /// surface is exchange-neutral (<c>string</c> pair, <see cref="ExchangeOrderSide"/>),
    /// but <see cref="Validate"/> internally uses Binance lot-size / notional
    /// filters via <see cref="BinanceSymbol"/>; that stays Binance-specific
    /// until we port the sizing logic to a neutral symbol-info type.
    /// </summary>
    public class MarketTradeRequest : ITradeRequest
    {
        /// <summary>
        /// Binance SDK symbol used for lot-size, tick-size, and notional
        /// filtering inside <see cref="Validate"/>. Internal only.
        /// </summary>
        public Symbol BinanceSymbol { get; set; }

        public string Symbol => BinanceSymbol; // implicit Symbol -> string
        public string BaseSymbol => BinanceSymbol.BaseAsset;
        public string QuoteSymbol => BinanceSymbol.QuoteAsset;
        public decimal QuoteClosePrice { get; internal set; }
        public DateTime? RequestDateTime { get; set; }
        public IStopLimitTracker StopLimitTracker { get; set; }
        public decimal QuoteQuantity { get; set; }
        public decimal BaseQuantity { get; set; }

        public bool Validate(decimal freeAmount, decimal nonFreeAmount)
        {
            //calculate quantity and stoploss limits
            var q = !FixedAmount ? (freeAmount + nonFreeAmount) * (decimal)Amount : (decimal)Amount;

            if (q > Volume * VolumeLimit)
            {
                q = Volume * VolumeLimit;
            }

            QuoteQuantity = AdjustForMinimum(BinanceSymbol.Price, q, MidpointRounding.ToZero);
            BaseQuantity = AdjustForMinimum(BinanceSymbol.Quantity, QuoteQuantity / QuoteClosePrice, MidpointRounding.ToZero); //btc

            var res = QuoteQuantity > BinanceSymbol.NotionalMinimumValue && CheckFee(BaseQuantity, QuoteClosePrice - StopLimitTracker.StopLimitPrice, QuoteQuantity);
            return res;
        }

        private bool CheckFee(decimal baseQuantity, decimal priceDiff, decimal quoteQuantity)
        {
            return quoteQuantity * 0.002m < (Math.Abs(priceDiff) * baseQuantity);
        }

        private decimal AdjustForMinimum(InclusiveRange symbolQuantity, decimal calculateQuantity, MidpointRounding rounding)
        {
            int precision = (int)Math.Round(-Math.Log10((double)symbolQuantity.Increment), 0);
            return Decimal.Round(calculateQuantity, precision, rounding);
        }

        public bool FixedAmount { get; set; }
        public decimal Amount { get; set; }
        public decimal Volume { get; set; }

        public decimal VolumeLimit { get; set; }
        public IExecutionStrategy Strategy { get; set; }
        public int Leverage { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
        public ExchangeOrderSide OrderSide { get => throw new NotImplementedException(); set => throw new NotImplementedException(); }
    }
}
