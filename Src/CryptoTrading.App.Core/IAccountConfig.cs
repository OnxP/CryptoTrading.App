using System.Collections.Generic;
using System.Threading.Tasks;
using CryptoTrading.App.Core.Exchange;

namespace CryptoTrading.App.Core
{
    /// <summary>
    /// Account-side configuration surface. Returns neutral types so
    /// consumers never have to know whether the backend is bundled
    /// Binance, Binance.Net, Bitfinex or an in-memory DB stub.
    /// </summary>
    public interface IAccountConfig
    {
        /// <summary>
        /// Pair strings (e.g. "BTCUSDT") that this account will trade.
        /// </summary>
        Task<List<string>> LoadCurrencies();

        /// <summary>
        /// Per-asset balances available to this account.
        /// </summary>
        Task<List<ExchangeBalance>> LoadPositions();
    }
}
