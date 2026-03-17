using Microsoft.AspNetCore.Mvc;

namespace CryptoTrading.App.Api.Controllers
{
    /// <summary>
    /// View backtest and optimization results.
    /// </summary>
    [ApiController]
    [Route("api/backtest-results")]
    public class BacktestResultsController : ControllerBase
    {
        [HttpGet]
        public ActionResult GetResults([FromQuery] string strategy = null, [FromQuery] int limit = 20)
        {
            // TODO: Query BacktestResults table
            return Ok(new { results = new object[] { } });
        }

        [HttpGet("{id}")]
        public ActionResult GetResult(int id)
        {
            // TODO: Get detailed backtest result with all metrics
            return Ok(new { id, result = (object)null });
        }
    }
}
