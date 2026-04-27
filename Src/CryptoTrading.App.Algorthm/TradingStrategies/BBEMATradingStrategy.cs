using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Exchange;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using IndicatorSetUp = CryptoTrading.App.Core.IndicatorSetUp;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class BBEMATradingStrategy : TradingStrategy
    {
        public BBEMATradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }

        public BBEMATradingStrategy(ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
        {
            noOfTrades = NoOfTrades;
        }

        protected override double StrategyWeight => 1.0 / noOfTrades;
        private double noOfTrades = 1d;


        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            var bbands = new IndicatorSetUp(Tulip.Indicators.bbands, new double[] { 20,2 });
            var ema = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 50 });
            var srsi = new IndicatorSetUp(Tulip.Indicators.stochrsi2, new double[] { 14, 14, 3, 3 });
            var sema = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 25 });
            dict.Add("close", new IndicatorSetUp(Tulip.Indicators.close, new double[] { 12 }));
            dict.Add("BBands", bbands);
            dict.Add("LongEma", ema);
            dict.Add("Ema", sema);
            dict.Add("sRsi", srsi);
            return dict;            
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, ExchangeCandlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var lower = GetIndicator(indicatorOutputs,"BBands", 0);
            var middle = GetIndicator(indicatorOutputs, "BBands", 1);
            var upper = GetIndicator(indicatorOutputs, "BBands", 2);
            var longEma = GetIndicator(indicatorOutputs, "Ema", 0);
            var kLine = GetIndicator(indicatorOutputs, "sRsi", 0);
            var dLine = GetIndicator(indicatorOutputs, "sRsi", 1);
            var close = GetIndicator(indicatorOutputs, "close", 0);
            var volume = GetIndicator(indicatorOutputs, "close", 1);
            var high = GetIndicator(indicatorOutputs, "close", 2);
            var low = GetIndicator(indicatorOutputs, "close", 3);
            var open = GetIndicator(indicatorOutputs, "close", 4);

            //open up a potential trade
            //might need to do some relative rounding e.g. eth
            //to open a window the price has to touch the bottom BB, then that opens a potential window.
            //Question how big should the window be? 12 candlesticks this is a key point to help filter bad signals.

            //then there has to be a close above the 50EMA.

            //the next candle stick has to be completly above the 50ema.

            //Entry CandleStick
            //then the next one has to be retest buy having a low below the 50EMA but close and open above the 50EMA.
            //also the high cannot be above the upper BB.
            //also can look at the RSI, on the entry candlestick look at -ve SRSI

            //stoploss- lower BB.
            //Target - +1.5 RR
            //reset SL to +1 RR then trail by 0.5RR

            //POV is on the entry candle stick then you can look at the historical data to valdate the entry single.

            var entryCandleStick = //kLine.First() < dLine.First() && 
                                   (double)closePrice.High < upper.First() &&
                                   (double)closePrice.Low < longEma.First() && 
                                   (double)closePrice.Open > longEma.First() && 
                                   (double)closePrice.Close > longEma.First();

            var previousCandleStick = low.Skip(1).First() > longEma.First();

            bool bottomBBand = false;

            for (int i = 2; i < close.Count-1; i++)
            {
                bottomBBand = low[i] < lower[i] && close[i] < longEma[i];

                if (bottomBBand == true) break;
            }
            
            if (entryCandleStick && previousCandleStick)
            {
                SetStopLimit(closePrice, StopLimitTrackers,lower.First(),1.5,0.5);
                return 1;
            }
            return 0;
        }
    }
}
