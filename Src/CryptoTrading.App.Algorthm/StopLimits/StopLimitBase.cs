using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.StopLimits
{
    public class StopLimitBase : IStopLimitTracker
    {
        public StopLimitBase() { }

        public decimal StopLimitPrice
        { get ;
            set ; }
        public decimal TargetPrice { get ; set ; }
        public decimal CurrentPrice { get ; set ; }
        public DateTime EndDateTime { get ; set ; }
        public bool IsOpen { get ; set ; }
        public decimal Increment { get ; set ; }
        public decimal Risk { get ; set ; }
        public decimal Multiple { get; set; }
        public string Pair { get; set; }

        public virtual void Configure(ExchangeOrder order)
        {
            IsOpen = true;
        }

        public void Close()
        {
            IsOpen = false;
        }

        public virtual void ManualChangeSL(decimal sl)
        {

        }

        public virtual void SetLimits(decimal quoteClosePrice)
        {

        }
        public virtual int SetStopLimit(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice, bool conditions, Action<string> logInformation)
        {
            if (conditions)
            {
                return SetStopLimit(indicatorOutputs, closePrice, logInformation) ? 1 : 0;
            }

            if (!IsOpen) return 0;
            //move stoplimit
            UpdateStopLimit(indicatorOutputs,closePrice, logInformation);
            return 0;
        }

        public virtual int SetSwingLowStopLimit(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice, bool conditions, bool closeConditions,decimal last10Low, Action<string> logInformation)
        {
            if (conditions)
            {
                return SetSwingLowStopLimit(indicatorOutputs, closePrice,last10Low, logInformation) ? 1 : 0;
            }

            if (!IsOpen) return 0;
            //move stoplimit
            UpdateSwingLowStopLimit(indicatorOutputs, closePrice, closeConditions, logInformation);
            return 0;
        }

        protected virtual bool SetStopLimit(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice, Action<string> logInformation)
        {
            if (IsOpen) return false;
            //var diff = closePrice.Close - Last10Low;
            var atr = indicatorOutputs["atr"][0].ToList();
            //volume and profitability conditions.
            if (closePrice.QuoteVolume <= 2.0m || closePrice.NumberOfTrades <= 10 ||
                ExchangeSymbolCache.Instance.Get(closePrice.Symbol).TickSize * 4 >= 2 * (decimal)atr.Last()) return false;

            var sl = closePrice.Close - 3m * (decimal)atr.Last();
            if (sl / closePrice.Close < 0.99m) sl = closePrice.Close * 0.99m;

            CurrentPrice = closePrice.Close;
            Pair = closePrice.Symbol;
            Multiple =
                AdjustForMinimum(ExchangeSymbolCache.Instance.Get(closePrice.Symbol).TickSize,
                    (decimal)atr
                        .Last());
            TargetPrice = AdjustForMinimum(ExchangeSymbolCache.Instance.Get(closePrice.Symbol).TickSize,1.02m *closePrice.Close);
            StopLimitPrice = AdjustForMinimum(ExchangeSymbolCache.Instance.Get(closePrice.Symbol).TickSize,sl);

            //StopLimitPrice = closePrice.Low;
            logInformation($"DateTime: {closePrice.CloseTime:G}|Symbol: {Pair}| Close: {CurrentPrice:0.00000000}| Target: {TargetPrice:0.00000000}| Stop: {StopLimitPrice:0.00000000}");
            return true;

        }

        private decimal tradePrice;
        protected virtual bool SetSwingLowStopLimit(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice,decimal last10Low, Action<string> logInformation)
        {
            if (IsOpen) return false;
            //var diff = closePrice.Close - Last10Low;
            var diff = closePrice.Close - last10Low;
            var perc = (diff / closePrice.Close) * 100;
            //volume and profitability conditions.
            if ((closePrice.QuoteVolume <= 4.0m && closePrice.NumberOfTrades <= 30 &&
                 ExchangeSymbolCache.Instance.Get(closePrice.Symbol).TickSize * 4 < 2 * diff) && Math.Abs(perc) >2) return false;

            CurrentPrice = closePrice.Close;
            tradePrice = closePrice.Close;
            Pair = closePrice.Symbol;
            Multiple =
                AdjustForMinimum(ExchangeSymbolCache.Instance.Get(closePrice.Symbol).TickSize,
                    1.5m * diff);
            TargetPrice = AdjustForMinimum(ExchangeSymbolCache.Instance.Get(closePrice.Symbol).TickSize,
                closePrice.Close + diff * 15m);
            StopLimitPrice = AdjustForMinimum(ExchangeSymbolCache.Instance.Get(closePrice.Symbol).TickSize,
                closePrice.Close - (decimal)diff);

            logInformation($"DateTime: {closePrice.CloseTime:G}|Symbol: {Pair}| Close: {CurrentPrice:0.00000000}| Target: {TargetPrice:0.00000000}| Stop: {StopLimitPrice:0.00000000}");
            return true;

        }

        protected virtual void UpdateStopLimit(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice, Action<string> logInformation)
        {
            var atr = indicatorOutputs["atr"][0].ToList();
            var currentSl = closePrice.Close - 1m * (decimal)atr.Last();
            if (currentSl > StopLimitPrice && currentSl < closePrice.Close)
            {
                logInformation($"Increasing Stoplimit for {closePrice.Symbol} at {closePrice.CloseTime}- Price: {closePrice.Close}  Current Limit: {StopLimitPrice} New Limit: {currentSl}");
                ManualChangeSL(currentSl);
            }
        }

        protected virtual void UpdateSwingLowStopLimit(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice,bool closeConditions, Action<string> logInformation)
        {
            var currentSl = closePrice.Close;
            if (closeConditions && tradePrice > StopLimitPrice)
            {
                logInformation($"Increasing Stoplimit for {closePrice.Symbol} at {closePrice.CloseTime}- Price: {closePrice.Close}  Current Limit: {StopLimitPrice} New Limit: {currentSl}");
                ManualChangeSL(currentSl);

            }
        }

        protected decimal AdjustForMinimum(decimal increment, decimal calculateQuantity)
        {
            int precision = (int)Math.Round(-Math.Log10((double)increment), 0);
            return decimal.Round(calculateQuantity, precision, MidpointRounding.AwayFromZero);
        }


        public virtual void MoveStopLimit()
        {
            StopLimitPrice = CurrentPrice;
        }

        public virtual bool RequestUpdateOfStopLimit(decimal closePrice)
        {
            return TargetPrice < closePrice;
        }
    }
}
