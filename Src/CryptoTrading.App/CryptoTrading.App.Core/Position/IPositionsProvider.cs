using CryptoTrading.App.Core.Position;
using System.Collections.Generic;

namespace CryptoTrading.App.Monitor
{
    public interface IPositionsProvider
    {
        Dictionary<string, IPosition> Load();
    }
}