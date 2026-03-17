using Microsoft.AspNetCore.Mvc;

namespace CryptoTrading.App.Api.Controllers
{
    /// <summary>
    /// Exchange management - view balances, positions, and trigger account refresh.
    /// </summary>
    [ApiController]
    [Route("api/exchanges")]
    public class ExchangeController : ControllerBase
    {
        [HttpGet]
        public ActionResult GetExchanges()
        {
            // TODO: Query ExchangeConfigs table
            return Ok(new { exchanges = new object[] { } });
        }

        [HttpGet("{id}/balances")]
        public ActionResult GetBalances(string id)
        {
            // TODO: Get exchange balances via IExchangeProvider
            return Ok(new { exchangeId = id, balances = new object[] { } });
        }

        [HttpGet("{id}/positions")]
        public ActionResult GetPositions(string id)
        {
            // TODO: Get open positions for exchange
            return Ok(new { exchangeId = id, positions = new object[] { } });
        }

        [HttpGet("{id}/trades")]
        public ActionResult GetTrades(string id, [FromQuery] int limit = 50)
        {
            // TODO: Get trade history for exchange
            return Ok(new { exchangeId = id, trades = new object[] { } });
        }

        [HttpPost("{id}/refresh")]
        public ActionResult RefreshAccount(string id)
        {
            // TODO: Trigger manual account refresh via AccountRefreshService
            return Ok(new { exchangeId = id, status = "refreshing" });
        }

        [HttpGet("{id}/snapshots")]
        public ActionResult GetSnapshots(string id, [FromQuery] int limit = 30)
        {
            // TODO: Get balance snapshot history
            return Ok(new { exchangeId = id, snapshots = new object[] { } });
        }
    }
}
