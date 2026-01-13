using Skender.Stock.Indicators;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Algorithm
{
    public class MarketStructureStrategy : IMarketStructureStrategy
    {
        private QuoteHub<IQuote> _quoteHub;
        private EmaHub<IQuote> _ema9;
        private EmaHub<IQuote> _ema20;
        private EmaHub<IQuote> _ema50;
        private EmaHub<IQuote> _ema100;
        private AtrHub<IQuote> _atr;

        public void SetQuotes(QuoteHub<IQuote> quoteHubHigh)
        {
            _quoteHub = quoteHubHigh;
            _ema9 =_quoteHub.ToEma(9);
            _ema20 =_quoteHub.ToEma(20);
            _ema50 =_quoteHub.ToEma(50);
            _ema100 =_quoteHub.ToEma(100);
            _atr = _quoteHub.ToAtr(14);
            Rsi = _quoteHub.Quotes.ToRsi(14);
            Macd = _quoteHub.Quotes.ToMacd();
            Adx = _quoteHub.Quotes.ToAdx(14);
            Aroon =  _quoteHub.Quotes.ToAroon(14);
            BollengerBands = _quoteHub.Quotes.ToBollingerBands(20, 2);
        }

        public IReadOnlyList<EmaResult> Ema9 => _ema9.Results;
        public IReadOnlyList<EmaResult> Ema20 => _ema9.Results;
        public IReadOnlyList<EmaResult> Ema50 => _ema9.Results;
        public IReadOnlyList<EmaResult> Ema100 => _ema9.Results;
        public IReadOnlyList<AtrResult> Atr => _atr.Results;
        public IReadOnlyList<RsiResult> Rsi { get; set; }
        public IReadOnlyList<MacdResult> Macd { get; set; }
        public IReadOnlyList<AdxResult> Adx { get; set; }
        public IReadOnlyList<AroonResult> Aroon { get; set; }
        public IReadOnlyList<BollingerBandsResult> BollengerBands { get; set; }
        //Market Regime structure
        //BullMarket
        //BearMarket
        //RangingMarket

        //indicators
        //Adx
        //BollingerBands
        //Macd
        //Rsi
        //Aroon - for changes in trends
        //EMA 

        //Additinal Volatility Structure
        //HighVolatility
        //LowVolatility
        public IMarketStructureResult Calculate()
        {
            var result = new MarketStructureResult();

            if(Ema9.LastOrDefault().Value > Ema20.LastOrDefault().Value
                && Ema20.LastOrDefault().Value > Ema50.LastOrDefault().Value
                && Ema50.LastOrDefault().Value > Ema100.LastOrDefault().Value)
                result.MarketRegime = MarketRegime.BullMarket;


            if (Ema9.LastOrDefault().Value < Ema20.LastOrDefault().Value
                && Ema20.LastOrDefault().Value < Ema50.LastOrDefault().Value
                && Ema50.LastOrDefault().Value < Ema100.LastOrDefault().Value)
                result.MarketRegime = MarketRegime.BearMarket;

            //Alternative method to determine market regime is using the Arron indicator
            //when the Arron Up is above 70 and the Arron Down is below 30 we are in a strong uptrend
            //when the Arron Down is above 70 and the Arron Up is below 30 we are in a strong downtrend
            //when both are between 30 and 70 we are in a ranging market

            var aroonLast = Aroon.LastOrDefault();
            if (aroonLast != null)
            {
                if (aroonLast.AroonUp > 70 && aroonLast.AroonDown < 30)
                    result.MarketRegime = MarketRegime.BullMarket;
                else if (aroonLast.AroonDown > 70 && aroonLast.AroonUp < 30)
                    result.MarketRegime = MarketRegime.BearMarket;
                else
                    result.MarketRegime = MarketRegime.RangingMarket; 
            }

            //additional details to consider
            //when the market is in a range then highlight the high and low of the last candlestick
            //and look for opportunities to trade the range
            //if the market breaks out of the range but not the Bollinger bands then look for a retest of the breakout level

            //pull back or ranging market?
            //if the price is between the 9ema then the market is ranging 

            //there are different levels of pull backs in an uptrend
            //shallow pull back - price stays above 20ema (I would class this as a pull back)
            //deep pull back - price goes below 20ema but stays above 50ema (ranging)
            //trend change - price goes below 50ema (there is a short term down trend to this one)


            //we would consider that the price is in an up trend if the price is above the 20, 50 and 100 EMA
            //the ranging market would be when the emas are clustered together, a fast EMA would show crossovers a bit more cleanly
            //a downtrend would be when the price is below the 20, 50 and 100 EMA
            //the trick to this is identifying the trend change early enough. this is where the aroon indicator would come in handy

            //ranging market to up or down trend
            //look for Bollinger band squeeze followed by a breakout above or below the bands
            //confirm with adx above 25 and rising
            //we should notice volume picking up as well on the breakout candle

            //uptrend/down to ranging market
            //trick with this is not to confuse it with a pullback in an uptrend as the signals are very similar
            //look for price moving into the Bollinger bands after being outside for a period of time
            //look for trend weakening with adx falling below 25
            //bb bands should also start to contract and price should move to the middle band (but this may be lagging)
            //Volume should be falling off as well, we should see it dip below average volume levels




            //Volatility Structure
            //we can use the atr to determine high and low volatility periods
            //when the atr is above its moving average we can consider it high volatility


            //volatility in the market can have an impact on how likely the next candlestick is going to break out of a range of the current candle stick
            return result;
        }

        
    }
}