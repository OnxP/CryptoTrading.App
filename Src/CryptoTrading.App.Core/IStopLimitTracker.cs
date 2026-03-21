using CryptoTrading.App.Core.Exchange;
using System;
using System.Collections.Generic;

namespace CryptoTrading.App.Core
{
    public interface IStopLimitTracker
    {
        decimal StopLimitPrice { get; set; }
        decimal TargetPrice { get; set; }
        void Configure(ExchangeOrder order);
        void MoveStopLimit();
        void Close();
        decimal CurrentPrice { get; set; }
        public DateTime EndDateTime { get; set; }
        bool IsOpen { get; set; }
        decimal Increment { get; set; }
        decimal Risk { get; set; }
        decimal Multiple { get; set; }
        string Pair { get; set; }

        bool RequestUpdateOfStopLimit(decimal closePrice);
        void ManualChangeSL(decimal sl);
        void SetLimits(decimal quoteClosePrice);
        int SetStopLimit(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice, bool conditions, Action<string> logInformation);

        int SetSwingLowStopLimit(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice,
            bool conditions, bool closeConditions, decimal last10Low, Action<string> logInformation);
    }
}
