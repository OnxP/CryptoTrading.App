using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Binance;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;

namespace CryptoTrading.App.Process
{
    internal class PositionHelper
    {
        public static void CheckDifferences(IPositions tradeProcessorPositions, List<AccountBalance> positions)
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

        public static void AddPositions(List<Symbol> symbols, List<AccountBalance> accountPositions, IPositions tradeProcessorPositions)
        {
            foreach (var symbol in symbols)
            {
                tradeProcessorPositions.GetPosition(symbol);
            }

            foreach (var accountPosition in accountPositions)
            {
                var position = tradeProcessorPositions.GetPosition(accountPosition.Asset);
                position.CreateTransaction(accountPosition.Free);
            }
        }
    }
}
