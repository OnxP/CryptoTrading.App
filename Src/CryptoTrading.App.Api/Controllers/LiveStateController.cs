using Microsoft.AspNetCore.Mvc;

namespace CryptoTrading.App.Api.Controllers
{
    /// <summary>
    /// Live trading state and performance metrics.
    /// </summary>
    [ApiController]
    [Route("api")]
    public class LiveStateController : ControllerBase
    {
        [HttpGet("live/state")]
        public ActionResult GetLiveState()
        {
            // TODO: Return current algorithm state
            // - Current regime (Bull/Bear/Ranging)
            // - Active volatility regime
            // - Open trades count
            // - Last processed candle timestamp
            return Ok(new
            {
                regime = "Unknown",
                volatility = "Unknown",
                openTrades = 0,
                lastUpdate = (string)null
            });
        }

        [HttpGet("performance/summary")]
        public ActionResult GetPerformanceSummary()
        {
            // TODO: Calculate from completed trades
            return Ok(new
            {
                totalTrades = 0,
                winRate = 0.0,
                totalReturn = 0.0,
                sharpeRatio = 0.0,
                maxDrawdown = 0.0,
                profitFactor = 0.0
            });
        }
    }
}
