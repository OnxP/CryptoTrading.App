using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Monitor.Strategies.HtfRsiVolExpansion;
using CryptoTrading.App.Monitor.Strategies.RegimeBased;
using Microsoft.Extensions.Logging;
using Skender.Stock.Indicators;
using System;

namespace CryptoTrading.App.Monitor.Strategies
{
    public class StrategySelector
    {
        private readonly ILogger _logger;
        private HtfRsiTradingState _tradingState = new HtfRsiTradingState(10000m);

        public StrategySelector(ILogger logger)
        {
            _logger = logger;
        }

        public IExecutionStrategy CreateStrategy(ITradeSignal signal, QuoteHub<IQuote> quoteHub)
        {
            IExecutionStrategy strategy;

            if (!string.IsNullOrEmpty(signal.RecommendedEntryStrategy)
                && signal.RecommendedEntryStrategy.StartsWith("RegimeBased"))
            {
                strategy = CreateRegimeBasedStrategy(signal, quoteHub);
            }
            else
            {
                strategy = CreateHtfRsiStrategy(signal);
            }

            strategy.SetQuotes(quoteHub);
            return strategy;
        }

        private IExecutionStrategy CreateHtfRsiStrategy(ITradeSignal signal)
        {
            var setup = new HtfRsiVolExpansionSetup
            {
                Direction = signal.Direction,
                EntryPrice = signal.EntryPrice,
                StopLoss = signal.StopLoss,
                TakeProfit = signal.TakeProfit,
                AtrAtEntry = signal.AtrAtSignal,
                InitialRisk = signal.InitialRisk,
                HtfRsi = (double)(signal.HtfRsi ?? 50m),
                VolExpansionRatio = (double)(signal.VolExpansionRatio ?? 1m),
                ProbabilityScore = (int)(signal.ProbabilityScore ?? 50m),
                Quantity = signal.Quantity,
                Leverage = signal.Leverage
            };

            var strategy = new SimpleExecutionStrategy(setup, _tradingState);
            strategy.Quantity = signal.Quantity;
            return strategy;
        }

        private IExecutionStrategy CreateRegimeBasedStrategy(ITradeSignal signal, QuoteHub<IQuote> quoteHub)
        {
            var setup = new SetupResult
            {
                SetupType = ParseSetupType(signal.SetupType),
                Direction = signal.Direction,
                IsValid = true,
                EntryZoneHigh = signal.EntryPrice,
                EntryZoneLow = signal.EntryPrice,
                StopLoss = signal.StopLoss,
                TakeProfit = signal.TakeProfit,
                RiskRewardRatio = signal.InitialRisk > 0
                    ? Math.Abs(signal.TakeProfit - signal.EntryPrice) / signal.InitialRisk
                    : 2m,
                Confidence = signal.ProbabilityScore ?? 50m,
                RecommendedEntryStrategy = ParseEntryStrategy(signal.RecommendedEntryStrategy),
                RecommendedExitStrategy = ParseExitStrategy(signal.RecommendedExitStrategy)
            };

            var strategy = new RegimeBasedExecutionStrategy(quoteHub, setup);
            strategy.Quantity = signal.Quantity;
            return strategy;
        }

        private static SetupType ParseSetupType(string value)
        {
            if (Enum.TryParse<SetupType>(value, out var result))
                return result;
            return SetupType.None;
        }

        private static EntryStrategyType ParseEntryStrategy(string value)
        {
            if (string.IsNullOrEmpty(value)) return EntryStrategyType.MarketOnConfirmation;
            if (Enum.TryParse<EntryStrategyType>(value, out var result))
                return result;
            return EntryStrategyType.MarketOnConfirmation;
        }

        private static ExitStrategyType ParseExitStrategy(string value)
        {
            if (string.IsNullOrEmpty(value)) return ExitStrategyType.TrailingStop;
            if (Enum.TryParse<ExitStrategyType>(value, out var result))
                return result;
            return ExitStrategyType.TrailingStop;
        }
    }
}
