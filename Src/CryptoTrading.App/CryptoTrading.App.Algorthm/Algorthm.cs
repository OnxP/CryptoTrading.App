using Binance;
using Binance.Client;
using CryptoTrading.App.Algorthm.TradingStrategies;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.TradeRequest;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorthm
{
    public class Algorthm : IAlgorthm
    {
        public int NumberOfCandleSticksToKeep => tradingStrategies.Max(x=>x.OutputLength);
        public List<ITradingStrategy> tradingStrategies { get; }
        private OrderedFixedLengthList _closePrices;
        public Algorthm(List<ITradingStrategy> strategies)
        {
            tradingStrategies = strategies;
            _closePrices = new OrderedFixedLengthList(NumberOfCandleSticksToKeep);
        }
        public void ProcessHistoricMarketData(IEnumerable<Candlestick> candlesticks)
        {
            //want to reduce dependancy on the candle stick object=> may need to create my own.
            _closePrices.AddRange(candlesticks.Select(x => x.Close));

            var result = CalculateTradeStrategies();
            //log load algothrm is sucessful.

            //pass the results to the broker, the result indicated the percentage, 
            //likely hood of a profitable trade so may want to invest a higher amount.
            //
            //shouldn't really be doing it hear since this was just to ensure that all the indicatators can yield results.
        }

        public void ProcessLiveCandleStick(CandlestickEventArgs candlestickEventArgs)
        {
            _closePrices.Add(candlestickEventArgs.Candlestick.Close);
            var result = CalculateTradeStrategies();
            var request = RequestBuilder.BuildTradeRequest(result,candlestickEventArgs.Candlestick.Symbol);
            MessageBroker.Instance.Publish(this,request);
        }

        public double CalculateTradeStrategies()
        {
            double result = 0;
            foreach (var strategy in tradingStrategies)
            {
                //some strategied may required more than just the close price, particularly at higher timeframes, don't need dates assuming the ordered list will track that, so it might be worth converting it into a struct.
                result += strategy.Calculate(_closePrices);
            }
            return result;
        }
    }
}
