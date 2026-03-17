using System;
using Microsoft.AspNetCore.Mvc;

namespace CryptoTrading.App.Api.Controllers
{
    /// <summary>
    /// Provides chart data for the React dashboard.
    /// Serves candlesticks, regime zones, trade signals, and supply/demand zones.
    /// </summary>
    [ApiController]
    [Route("api/chart")]
    public class ChartDataController : ControllerBase
    {
        [HttpGet("{symbol}/candles")]
        public ActionResult GetCandles(string symbol, [FromQuery] string interval = "4h",
            [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            // TODO: Query CandleStickDbs table for OHLCV data
            return Ok(new { symbol, interval, candles = new object[] { } });
        }

        [HttpGet("{symbol}/regimes")]
        public ActionResult GetRegimes(string symbol, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            // TODO: Return regime zone data (Bull/Bear/Ranging periods)
            return Ok(new { symbol, regimes = new object[] { } });
        }

        [HttpGet("{symbol}/signals")]
        public ActionResult GetSignals(string symbol, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            // TODO: Return trade entry/exit signals
            return Ok(new { symbol, signals = new object[] { } });
        }

        [HttpGet("{symbol}/zones")]
        public ActionResult GetZones(string symbol, [FromQuery] DateTime? from = null, [FromQuery] DateTime? to = null)
        {
            // TODO: Return supply/demand zones
            return Ok(new { symbol, zones = new object[] { } });
        }
    }
}
