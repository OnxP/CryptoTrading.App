using System.Collections.Generic;
using System.Linq;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.Position;

namespace CryptoTrading.App.Process
{
    internal class PositionHelper
    {
        public static void CheckDifferences(IPositions tradeProcessorPositions, List<ExchangeBalance> positions)
        {
            foreach (var positionBinance in positions.Where(x=>x.Free!= 0.0m))
            {
                var position = tradeProcessorPositions.GetPosition(positionBinance.Asset);
                if (!position.HasOpenPosition)
                {
                    var diff = positionBinance.Free - position.FreeAmount;
                    if (diff != 0.0m)
                    {
                        position.CreateTransaction(diff);
                    }
                }
            }
        }

        public static void AddPositions(List<ExchangeSymbol> symbols, List<ExchangeBalance> accountPositions, IPositions tradeProcessorPositions)
        {
            foreach (var symbol in symbols)
            {
                tradeProcessorPositions.GetPosition(symbol.BaseAsset);
            }

            foreach (var accountPosition in accountPositions)
            {
                tradeProcessorPositions.AjdustPosition(accountPosition.Asset,accountPosition.Free);
            }
        }
    }
}
