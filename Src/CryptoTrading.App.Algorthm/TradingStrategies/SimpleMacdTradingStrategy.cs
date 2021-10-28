using Binance;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database.RunIndicators.Indicators;
using Microsoft.Extensions.Logging;
using System.Collections.Generic;
using System.Linq;
using Tulip;

namespace CryptoTrading.App.Algorthm.TradingStrategies
{
    public class SimpleMacdTradingStrategy : TradingStrategy
    {
        public SimpleMacdTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }
        public SimpleMacdTradingStrategy(ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
        {
            noOfTrades = NoOfTrades;
        }

        protected override double StrategyWeight => 1.0 / noOfTrades;
        private double noOfTrades = 1d;

        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();

            //add indicators to dictionary
            double shortPeriod = 12;
            double longPeriod = 26;
            double signal = 9;
            var macd = new IndicatorSetUp(Tulip.Indicators.macd, new double[] { shortPeriod, longPeriod,signal });
            var atr = new IndicatorSetUp(Tulip.Indicators.atr, new double[] { 14 });
            var ema = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 100 });
            var srsi = new IndicatorSetUp(Tulip.Indicators.stochrsi2, new double[] { 14, 14, 3, 3 });
            var close = new IndicatorSetUp(Tulip.Indicators.close, new double[] { 6 });

            dict.Add("MACD", macd);
            dict.Add("LongEma", ema);
            dict.Add("close", close);
            dict.Add("sRsi", srsi);
            dict.Add("atr", atr);
            return dict;
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var macd = indicatorOutputs["MACD"][0].ToList();
            var signal = indicatorOutputs["MACD"][1].ToList();
            var hist = indicatorOutputs["MACD"][2].ToList();
            var longEma = indicatorOutputs["LongEma"][0].ToList();
            var low = indicatorOutputs["close"][3].ToList();
            var atr = indicatorOutputs["atr"][0].ToList();

            var kLine = indicatorOutputs["sRsi"][0].ToList();
            var dLine = indicatorOutputs["sRsi"][1].ToList();
            //add rsi and don't make a trade when the rsi is above 60.
            //or remove condition4? or reduce the range
            longEma.Reverse();
            macd.Reverse();
            signal.Reverse();
            hist.Reverse();


            // log values
            
            var condition1 = macd.First() > signal.First();

            //var condition3 = kLine.First() <= 50.0 && kLine.First() >= dLine.First();
            //var condition3 = CheckMacdTail(macd,signal,hist,close);
            var condition2 = longEma.First() < (double)closePrice.Close;
            var condition4 = macd.First() < 0.0d;

            //&& longEma.First() < (double)closePrice.Open; //this checks for an up trend however doesn't check for sideways trend.
            //var condition5 = LastSixClose(close);
            //var condition3 = vWap.First() > longEma.Skip(1).First(); //this checks for an up trend however doesn't check for sideways trend.
            //var condition4 = macd.First() > macd.Skip(1).First();
            //var condition5 = macd.First() < 0;
            //Price > than Long EMA
            //Long EMA is in an uptrend
            //Fast > Slow EMA
            if (condition2 && condition1 && condition4)
            {
                //last 3 average
                //set the stop loss at the  low of the last 3 candle sticks
                if (CheckLastTrade(StopLimitTrackers.EndDateTime,closePrice.CloseTime,closePrice.Interval))
                {
                    LogResult(1);
                    StopLimitTrackers.StopLimitPrice = (decimal)(low.Take(5).Min() - (atr.Last() / 2));

                    return StrategyWeight;
                }                
            }
            if (StopLimitTrackers.IsOpen)
            {
                var sl = (decimal)(low.Take(5).Min() - (atr.Last() / 2));
                StopLimitTrackers.ManualChangeSL(sl);
            }
            //check if long is trading sideways, need more entries to determin that!

            //check if long is in an uptrend.
            LogResult(0);
            return 0;
        }
        //private TradeAction Macd(TradingPairInfo tradingPairInfo)
        //{
        //    var mcad = tradingPairInfo.macd;
        //    decimal rsi = tradingPairInfo.rsi.Value;
        //    var srsi = tradingPairInfo.stochRsi;
        //    var Sma5 = tradingPairInfo.Ema[5].Value;
        //    var Sma20 = tradingPairInfo.Ema[20].Value;
        //    var Sma100 = tradingPairInfo.Ema[20].Value;
        //    var Gsma20 = tradingPairInfo.Gsma[20].Value;

        //    if (mcad.Value > 0 && rsi < 50 && srsi.Value > 0 && Sma5 > Sma20 && Gsma20 > 0)
        //        return TradeAction.Buy;

        //    if (((Sma5 - Sma20) / Sma20) >= 40m)
        //        return TradeAction.Sell;

        //    if (mcad.Value <= 0 && srsi.Value < 0 && rsi > 80)
        //        return TradeAction.Sell;

        //    return TradeAction.Wait;
        //}

//        private void CalculateGsma()
//        {
//            List<decimal> smaList = new List<decimal>();
//            List<decimal> diff = new List<decimal>();
//            Queue<decimal> queue = new Queue<decimal>(averageLength + 1);
//for (int i = 0; i < _ema.ema.Count() - 1; i++)
//{
//diff.Add(_ema.ema.Skip(i).First() - _ema.ema.Skip(i + 1).First());
//}

//            _diff = diff;

//            for (int i = 0; i < _diff.Count() - averageLength + 1; i++)
//            {
//                smaList.Add(_diff.Skip(i).Take(averageLength).Sum() / averageLength);
//}

//            gsma = smaList;
//        }

//        public void AddCandleStick(Candlestick futureCandleStick)
//        {
//_ema.AddCandleStick(futureCandleStick);
//            CalculateCurrentSma();
//        }

//        private void CalculateCurrentSma()
//        {
//            var listdiff = new List<decimal>();
//            listdiff.Add(_ema.Value - _ema.ema.Skip(1).First());
//listdiff.AddRange(gsma.Take(100));
//            _diff = listdiff;
//            var sum = _diff.Take(averageLength).Sum();
//            var list = new List<decimal>();
//            list.Add(sum / averageLength);
//            list.AddRange(gsma.Take(100));
//            gsma = list;
//}
//}
    private bool CheckMacdTail(List<double> macd, List<double> signal, List<double> hist, double[] close)
        {
            //how to check that we are at the end of the trade window.
            //either set 5 candle sticks since the start for the window
            var condition1 = macd.First() > signal.First();
            //last 3 historgram points are getting smaller then don't trade
            //but only when the last 3 macd point are greater than the signal line.
            var condition2 = macd[0] > macd[1] && macd[1] > macd[2] && macd.First()>0.0d;
            var condition3 = macd[1] > signal[1] && macd[2] > signal[2];
            var condition4 = macd.First() < 0.0d;
            var condition5 = LastSixClose(close);
            //macdline last 4 candlesticks
            //



            return condition1;

        }

        private bool LastSixClose(double[] close)
        {
            if (close[0] < close[1] && close[1] < close[2] && close[2] < close[3] && close[3] < close[4] && close[4] < close[5])
                return true;
            else if (close[0] < close[1] && close[1] < close[2] && close[2] < close[3])
                return true;
            else if (close[1] < close[2] && close[2] < close[3] && close[3] < close[4] && close[4] < close[5])
                return true;
            else if (close[0] < close[1] && close[2] < close[3] && close[3] < close[4] && close[4] < close[5])
                return true;
            else if (close[0] < close[1] && close[1] < close[2] && close[3] < close[4] && close[4] < close[5])
                return true;
            else if (close[0] < close[1] && close[1] < close[2] && close[2] < close[3] && close[4] < close[5])
                return true;
            else if (close[0] < close[1] && close[1] < close[2] && close[2] < close[3] && close[3] < close[4])
                return true;
            else return false;
        }

    }
}
