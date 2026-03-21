using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc;

namespace CryptoTrading.App.Api.Controllers
{
    /// <summary>
    /// Manage strategy parameters - view, update, and reset to defaults.
    /// </summary>
    [ApiController]
    [Route("api/strategies")]
    public class StrategyParametersController : ControllerBase
    {
        [HttpGet]
        public ActionResult<IEnumerable<string>> GetStrategies()
        {
            // TODO: Query distinct StrategyName from StrategyParameters table
            return Ok(new[]
            {
                "RegimeBasedMarketStructure",
                "RegimeBasedSetup",
                "SupplyDemandZone",
                "StochRsiEntry",
                "TrailingStopExit",
                "ScaleOutExit",
                "TimeBasedExit"
            });
        }

        [HttpGet("{name}/parameters")]
        public ActionResult GetParameters(string name)
        {
            // TODO: Query StrategyParameters by StrategyName
            return Ok(new { strategyName = name, parameters = new object[] { } });
        }

        [HttpPut("{name}/parameters")]
        public ActionResult UpdateParameters(string name, [FromBody] Dictionary<string, double> parameters)
        {
            // TODO: Update StrategyParameters in DB
            return Ok(new { strategyName = name, updated = parameters.Count });
        }

        [HttpPost("{name}/parameters/reset")]
        public ActionResult ResetParameters(string name)
        {
            // TODO: Reset to defaults using StrategyParameterSeeder
            return Ok(new { strategyName = name, status = "reset" });
        }
    }
}
