using System.Collections.Generic;
using System.Linq;
using Binance;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Position;

namespace CryptoTrading.App.Process
{
    internal class PositionHelper
    {
        // `positions` is now neutral (ExchangeBalance) — any exchange adapter can
        // feed it. Only the Asset + Free fields are read, which both Binance
        // AccountBalance and ExchangeBalance expose identically.
        public static void CheckDifferences(IPositions tradeProcessorPositions, List<ExchangeBalance> positions)
        {
            foreach (var balance in positions.Where(x => x.Free != 0.0m))
            {
                var position = tradeProcessorPositions.GetPosition(balance.Asset);
                if (!position.HasOpenPosition)
                {
                    var diff = balance.Free - position.FreeAmount;
                    if (diff != 0.0m)
                    {
                        position.CreateTransaction(diff);
                    }
                }
            }
        }

        public static void AddPositions(List<Symbol> symbols, List<ExchangeBalance> accountPositions, IPositions tradeProcessorPositions)
        {
            foreach (var symbol in symbols)
            {
                tradeProcessorPositions.GetPosition(symbol.BaseAsset);
            }

            foreach (var accountPosition in accountPositions)
            {
                tradeProcessorPositions.AjdustPosition(accountPosition.Asset, accountPosition.Free);
            }
        }
    }
}
