using Binance;
using CryptoTrading.App.Core;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.StopLimits
{
    public class StopLimitBase : IStopLimitTracker
    {
        public decimal StopLimitPrice { get ; set ; }
        public decimal TargetPrice { get ; set ; }
        public decimal CurrentPrice { get ; set ; }
        public DateTime EndDateTime { get ; set ; }
        public bool IsOpen { get ; set ; }
        public decimal Increment { get ; set ; }
        public decimal Risk { get ; set ; }
        public decimal Multiple { get; set; }
        public string Pair { get; set; }

        public virtual void Configure(Order order)
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
        public virtual int SetStopLimit(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, bool conditions, Action<string> logInformation)
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

        protected virtual bool SetStopLimit(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, Action<string> logInformation)
        {
            if (IsOpen) return false;
            //var diff = closePrice.Close - Last10Low;
            var atr = indicatorOutputs["atr"][0].ToList();
            //volume and profitability conditions.
            if (closePrice.QuoteAssetVolume <= 2.0m || closePrice.NumberOfTrades <= 10m ||
                Symbol.Cache.Get(closePrice.Symbol).Price.Increment * 4 >= 2 * (decimal)atr.Last()) return false;
                
            CurrentPrice = closePrice.Close;
            Pair = closePrice.Symbol;
            Multiple =
                AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price,
                    (decimal)atr
                        .Last());
            TargetPrice = AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price,
                closePrice.Close + Risk * (decimal)atr.Last());
            StopLimitPrice = AdjustForMinimum(Symbol.Cache.Get(closePrice.Symbol).Price,
                closePrice.Close - Risk * (decimal)atr.Last());
            logInformation($"DateTime: {closePrice.CloseTime:G}|Symbol: {Pair}| Close: {CurrentPrice:0.00000000}| Target: {TargetPrice:0.00000000}| Stop: {StopLimitPrice:0.00000000}");
            return true;

        }

        protected virtual void UpdateStopLimit(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, Action<string> logInformation)
        {
            var atr = indicatorOutputs["atr"][0].ToList();
            var currentSl = closePrice.Close - Risk * (decimal)atr.Last();
            if (currentSl > StopLimitPrice && currentSl < closePrice.Close)
            {
                logInformation($"Increasing Stoplimit for {closePrice.Symbol} at {closePrice.CloseTime}- Price: {closePrice.Close}  Current Limit: {StopLimitPrice} New Limit: {currentSl}");
                ManualChangeSL(currentSl);
            }
        }

        protected decimal AdjustForMinimum(InclusiveRange symbolQuantity, decimal calculateQuantity)
        {
            int precision = (int)Math.Round(-Math.Log10((double)symbolQuantity.Increment), 0);
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
