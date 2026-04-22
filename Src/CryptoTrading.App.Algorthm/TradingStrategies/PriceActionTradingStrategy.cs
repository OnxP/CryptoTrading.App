using Binance;
using CryptoTrading.App.Algorithm.CustomIndicators;
using CryptoTrading.App.Core;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm.TradingStrategies
{
    public class PriceActionTradingStrategy : TradingStrategy
    {
        private readonly int ZigZagCandles = 10;
        private readonly int MBFXCandles = 10;
        private readonly int MovingAveragePeriod = 15;

        private ZigZag _zigZag = new ZigZag();
        private Mbfx _mbfx = new Mbfx();
        private TrendLine _trendLine = new TrendLine();
        private SupportResistance _supportResistance;
        private Signal _signal = new Signal();
        public PriceActionTradingStrategy(ILogger<TradingStrategy> logger) : base(logger)
        {
        }
        public PriceActionTradingStrategy(ILogger<TradingStrategy> logger, double NoOfTrades) : this(logger)
        {
            noOfTrades = NoOfTrades;
        }

        protected override double StrategyWeight => 1.0 / noOfTrades;
        private double noOfTrades = 1d;

        //public override int OutputLength => 1000;

        protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
        {
            var dict = new Dictionary<string, IndicatorSetUp>();
            dict.Add("PSAR", new IndicatorSetUp(Tulip.Indicators.psar, new double[] { 0.02, 0.2 }));
            dict.Add("100", new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 100 }));
            _signal.Indicators.Add(new CustomIndicators.Indicator("ZigZag"));
            _signal.Indicators.Add(new CustomIndicators.Indicator("MBFX"));
            _signal.Indicators.Add(new CustomIndicators.Indicator("Trend"));
            _signal.Indicators.Add(new CustomIndicators.Indicator("MA15"));
            _signal.Indicators.Add(new CustomIndicators.Indicator("S&R"));
            return dict;
        }
        public Signal Process(List<Candlestick> Candles)
        {
            if (Candles == null) return _signal;
            if (Candles.Count == 0) return _signal;
            _zigZag.Refresh(Candles);
            _mbfx.Refresh(Candles);
            _trendLine.Refresh(Candles);

            int zigZagBar = -1;
            bool zigZagBuy = false;
            bool zigZagSell = false;
            bool mbfxOk = false;
            bool trendOk = false;
            bool sma15Ok = false;
            decimal zigZagPrice = 0M;

            // Rule #1: a zigzar arrow appears
            for (int bar = ZigZagCandles; bar >= 1; bar--)
            {
                var arrow = _zigZag.GetArrow(bar);
                if (arrow == ArrowType.Buy)
                {
                    zigZagBuy = true;
                    zigZagSell = false;
                    zigZagBar = bar;
                    zigZagPrice = Candles[bar].Low;
                }

                //if (arrow == ArrowType.Sell)
                //{
                //    break;
                //    //zigZagBuy = false;
                //    //zigZagSell = true;
                //    //zigZagBar = bar;
                //    //zigZagPrice = Candles[bar].High;
                //}
            }

            // BUY signals
            if (zigZagBuy && zigZagBar > 0)
            {
                // MBFX should be green at the moment
                // and should have been below < 30 some candles ago
                int barStart = zigZagBar;
                if (zigZagBar == 1) barStart = 2;
                for (int bar = Math.Min(barStart, MBFXCandles); bar >= 1; bar--)
                {
                    var red = _mbfx.RedValue(bar);
                    var green = _mbfx.GreenValue(bar);
                    if (red < 30M || green < 30M)
                    {
                        mbfxOk = true;
                    }
                }

                // trend line should be green at the moment
                if (_trendLine.IsGreen(1))
                {
                    trendOk = true;
                }

                // rule #4: price should be above 15 SMA
                var ma1 = MovingAverage.Get(Candles, 15, MovingAveragePeriod, MaMethod.Sma, AppliedPrice.Close);
                if (Candles[1].Close > ma1)
                {
                    sma15Ok = true;
                }
            }
            
            // SELL signals
            if (zigZagSell && zigZagBar > 0)
            {
                // MBFX should now be red
                // and should been above > 70 some candles ago
                int barStart = zigZagBar;
                if (zigZagBar == 1) barStart = 2;
                barStart = Math.Min(barStart, MBFXCandles);
                for (int bar = barStart; bar >= 1; bar--)
                {
                    var red = _mbfx.RedValue(bar);
                    var green = _mbfx.GreenValue(bar);
                    if ((red > 70M && red < 200M) || (green > 70M && green < 200M))
                    {
                        mbfxOk = true;
                    }
                }

                // trend line should now be red
                if (_trendLine.IsRed(1))
                {
                    trendOk = true;
                }

                // rule #4: and price below SMA15 on previous candle
                var ma1 = MovingAverage.Get(Candles, 1, MovingAveragePeriod, MaMethod.Sma, AppliedPrice.Close);
                if (Candles[1].Close < ma1)
                {
                    sma15Ok = true;
                }
            }
            
            
            // set indicators
            if (zigZagBar >= 1 && (zigZagBuy || zigZagSell))
            {
                _signal.Indicators[0].IsValid = true;
                _signal.Indicators[1].IsValid = mbfxOk;
                _signal.Indicators[2].IsValid = trendOk;
                _signal.Indicators[3].IsValid = sma15Ok;
                var average = AverageCandleSize(Candles);
                _signal.Indicators[4].IsValid = _supportResistance.IsAtSupportResistance(zigZagPrice, average);
                _signal.Type = zigZagBuy ? SignalType.Buy : SignalType.Sell;
                if (_signal.Indicators[4].IsValid)
                {
                    if (zigZagBuy)
                    {
                        _signal.CloseAtPrice = _supportResistance.GetNextResistanceLevel(zigZagPrice, average);
                    }
                    else if (zigZagSell)
                    {
                        _signal.CloseAtPrice = _supportResistance.GetNextSupportLevel(zigZagPrice, average);
                    }
                }
                
            }
            
            else
            {
                _signal.Type = SignalType.None;
            }
            return _signal;
        }

        public decimal AverageCandleSize(List<Candlestick> Candles)
        {
            decimal AverageCandleSize = 0.0m;
            for (int i = 1; i < Candles.Count; ++i)
            {
                AverageCandleSize += Math.Abs(Candles[i].High - Candles[i].Low);
            }
            AverageCandleSize /= ((decimal)(Candles.Count));
            return AverageCandleSize;
        }
        public override double Calculate(CandleStickDictionary closePrices, IStopLimitTracker StopLimitTrackers)
        {
            var psar = base.Calculate(closePrices,StopLimitTrackers);
            var candles = closePrices.Values
                .Select(c => ExchangeCandlestickBridge.ToBundled(c, (CandlestickInterval)(int)closePrices.Interval))
                .ToList();
            candles.Reverse();
            _supportResistance = new SupportResistance(candles);

            _signal = Process(candles);
            var validCount = _signal.Indicators.Count(e => e.IsValid);
            if (validCount == 5 && _signal.Type == SignalType.Buy)
            {
                LogResult(1);
                StopLimitTrackers.StopLimitPrice = Convert.ToDecimal(psar);
                StopLimitTrackers.TargetPrice = _signal.CloseAtPrice;
                return StrategyWeight;
            }
            else
            {
                //if (StopLimitTrackers.IsOpen && validCount == 5 && _signal.Type == SignalType.Sell)
                //{
                //    StopLimitTrackers.TargetPrice = closePrices.Current.Close;
                //    LogResult(-1);
                //}
                return 0;
            }
        }

        protected override double Calculate(Dictionary<string, double[][]> indicatorOutputs, Candlestick closePrice, IStopLimitTracker StopLimitTrackers)
        {
            var pSar = indicatorOutputs["PSAR"][0].ToList();
            return pSar.Last();//does nothing
        }
    }
}
