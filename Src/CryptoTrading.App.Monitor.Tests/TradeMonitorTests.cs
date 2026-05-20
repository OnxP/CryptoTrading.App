using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Monitor;
using CryptoTrading.App.Monitor.Position;
using Microsoft.Extensions.Logging;
using Moq;
using Skender.Stock.Indicators;

namespace CryptoTrading.App.Tests.Monitor
{
    [TestClass]
    public class TradeMonitorTests
    {
        private Mock<ILogger<TradeMonitor>> _mockLogger = new();
        private Mock<IMarketMonitor> _mockMarketMonitor = new();
        private Mock<IConfig> _mockConfig = new();
        private Mock<IBroker> _mockBroker = new();

        private ITradeSignal _testSignal;

        [TestInitialize]
        public void TestInitialize()
        {
            _mockLogger = new Mock<ILogger<TradeMonitor>>();
            _mockMarketMonitor = new Mock<IMarketMonitor>();
            _mockConfig = new Mock<IConfig>();
            _mockBroker = new Mock<IBroker>();
            _mockBroker.Setup(b => b.SubmitMarketOrder(It.IsAny<string>(), It.IsAny<ExchangeOrderSide>(), It.IsAny<decimal>()))
                .ReturnsAsync(OrderHelper.CreateOrder(99999));
            _mockBroker.Setup(b => b.CancelOrder(It.IsAny<string>(), It.IsAny<string>()))
                .ReturnsAsync(new ExchangeOrder { Status = ExchangeOrderStatus.Cancelled });

            _testSignal = new TradeSignal
            {
                Symbol = "BTCUSDT",
                BaseSymbol = "BTC",
                QuoteSymbol = "USDT",
                Direction = TradeDirection.Long,
                Leverage = 5,
                Quantity = 0.01m,
                SignalTime = DateTime.Now,
                EntryPrice = 50000m,
                StopLoss = 49000m,
                TakeProfit = 52000m,
                AtrAtSignal = 500m,
                InitialRisk = 750m,
                SetupType = "HtfRsiVolExpansion",
                RecommendedEntryStrategy = "Simple",
                RecommendedExitStrategy = "Simple"
            };
        }

        private TradeMonitor CreateMonitor() =>
            new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object, _mockBroker.Object);

        [TestMethod]
        public void Constructor_InitializesCorrectly()
        {
            var monitor = CreateMonitor();

            Assert.IsNotNull(monitor);
            Assert.IsNotNull(monitor.CompletedTrades);
            Assert.IsEmpty(monitor.CompletedTrades);
            Assert.AreEqual("1", monitor.KeyValue);
        }

        [TestMethod]
        public void KeyValue_CanBeSet()
        {
            var monitor = CreateMonitor();

            monitor.KeyValue = "CustomKey";

            Assert.AreEqual("CustomKey", monitor.KeyValue);
        }

        [TestMethod]
        public void AcceptSignal_SetsSignalAndSymbol()
        {
            var monitor = CreateMonitor();

            monitor.AcceptSignal(_testSignal);

            Assert.AreEqual(_testSignal, monitor.Signal);
            Assert.AreEqual("BTCUSDT", monitor.Symbol);
        }

        [TestMethod]
        public void Live_ReturnsFalseWhenNoPosition()
        {
            var monitor = CreateMonitor();
            monitor.AcceptSignal(_testSignal);

            Assert.IsFalse(monitor.Live);
        }

        [TestMethod]
        public void Symbol_ReturnsSignalSymbol()
        {
            var monitor = CreateMonitor();
            monitor.AcceptSignal(_testSignal);

            Assert.AreEqual("BTCUSDT", monitor.Symbol);
        }

        [TestMethod]
        public void ToString_ReturnsFormattedString()
        {
            var monitor = CreateMonitor();
            monitor.AcceptSignal(_testSignal);
            monitor.currentCloseTime = new DateTime(2024, 1, 15, 10, 30, 0);

            var result = monitor.ToString();

            Assert.Contains("BTCUSDT", result);
            Assert.Contains("2024-01-15", result);
        }

        [TestMethod]
        public async Task SubscribetToMarketData_SubscribesWhenNotAlreadySubscribed()
        {
            var monitor = CreateMonitor();
            monitor.AcceptSignal(_testSignal);

            var candleSticks = new List<ExchangeCandlestick>
            {
                CandleStickHelper.CreateCandlestick(DateTime.Now, 50000, 51000, 49000, 50500, 100)
            };

            _mockMarketMonitor.Setup(m => m.IsSubscribed(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
            _mockMarketMonitor.Setup(m => m.GetHistoricCandleSticks(It.IsAny<string>())).ReturnsAsync(candleSticks);

            await monitor.SubscribetToMarketData();

            _mockMarketMonitor.Verify(m => m.GetHistoricCandleSticks("BTCUSDT"), Times.Once);
            _mockMarketMonitor.Verify(m => m.Subscribe(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Action<ExchangeCandlestickEvent>>()), Times.Once);
        }

        [TestMethod]
        public async Task SubscribetToMarketData_DoesNotSubscribeWhenAlreadySubscribed()
        {
            var monitor = CreateMonitor();
            monitor.AcceptSignal(_testSignal);

            _mockMarketMonitor.Setup(m => m.IsSubscribed(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

            await monitor.SubscribetToMarketData();

            _mockMarketMonitor.Verify(m => m.GetHistoricCandleSticks(It.IsAny<string>()), Times.Never);
            _mockMarketMonitor.Verify(m => m.Subscribe(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Action<ExchangeCandlestickEvent>>()), Times.Never);
        }

        [TestMethod]
        public async Task SubscribetToMarketData_ProcessesHistoricCandlesticksCorrectly()
        {
            var monitor = CreateMonitor();
            monitor.AcceptSignal(_testSignal);

            var candleSticks = new List<ExchangeCandlestick>
            {
                CandleStickHelper.CreateCandlestick(DateTime.Now.AddMinutes(-2), 50000, 51000, 49000, 50500, 100),
                CandleStickHelper.CreateCandlestick(DateTime.Now.AddMinutes(-1), 50500, 51500, 50000, 51000, 100)
            };

            _mockMarketMonitor.Setup(m => m.IsSubscribed(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
            _mockMarketMonitor.Setup(m => m.GetHistoricCandleSticks(It.IsAny<string>())).ReturnsAsync(candleSticks);

            await monitor.SubscribetToMarketData();

            _mockMarketMonitor.Verify(m => m.GetHistoricCandleSticks("BTCUSDT"), Times.Once);
        }

        [TestMethod]
        public async Task SetNewSignal_SameDirection_UpdatesSignal()
        {
            var monitor = CreateMonitor();
            monitor.AcceptSignal(_testSignal);

            var newSignal = new TradeSignal
            {
                Symbol = "BTCUSDT",
                BaseSymbol = "BTC",
                QuoteSymbol = "USDT",
                Direction = TradeDirection.Long,
                Quantity = 0.02m,
                SignalTime = DateTime.Now,
                EntryPrice = 51000m,
                StopLoss = 50000m,
                TakeProfit = 53000m,
                AtrAtSignal = 500m,
                InitialRisk = 750m
            };

            await monitor.SetNewSignal(newSignal);

            Assert.AreEqual(newSignal, monitor.Signal);
        }

        [TestMethod]
        public async Task SetNewSignal_ChangeDirection_NoPosition_UpdatesSignalOnly()
        {
            var monitor = CreateMonitor();
            monitor.AcceptSignal(_testSignal);

            var newSignal = new TradeSignal
            {
                Symbol = "BTCUSDT",
                BaseSymbol = "BTC",
                QuoteSymbol = "USDT",
                Direction = TradeDirection.Short,
                Quantity = 0.01m,
                SignalTime = DateTime.Now,
                EntryPrice = 50000m,
                StopLoss = 51000m,
                TakeProfit = 48000m,
                AtrAtSignal = 500m,
                InitialRisk = 750m
            };

            await monitor.SetNewSignal(newSignal);

            Assert.AreEqual(newSignal, monitor.Signal);
            _mockBroker.Verify(b => b.SubmitMarketOrder(It.IsAny<string>(), It.IsAny<ExchangeOrderSide>(), It.IsAny<decimal>()), Times.Never);
        }

        [TestMethod]
        public void CurrentStopLimit_CanBeSetAndRetrieved()
        {
            var monitor = CreateMonitor();
            decimal expectedValue = 50000.50m;

            monitor.CurrentStopLimit = expectedValue;

            Assert.AreEqual(expectedValue, monitor.CurrentStopLimit);
        }

        [TestMethod]
        public void CurrentCloseTime_CanBeSetAndRetrieved()
        {
            var monitor = CreateMonitor();
            var expectedTime = new DateTime(2024, 1, 15, 10, 30, 0);

            monitor.currentCloseTime = expectedTime;

            Assert.AreEqual(expectedTime, monitor.currentCloseTime);
        }

        [TestMethod]
        public void MarketMonitor_CanBeAccessed()
        {
            var monitor = CreateMonitor();

            var marketMonitor = monitor.marketMonitor;

            Assert.IsNotNull(marketMonitor);
            Assert.AreEqual(_mockMarketMonitor.Object, marketMonitor);
        }

        [TestMethod]
        public void CompletedTrades_StartsEmpty()
        {
            var monitor = CreateMonitor();

            Assert.IsNotNull(monitor.CompletedTrades);
            Assert.IsEmpty(monitor.CompletedTrades);
        }
    }

    class OrderHelper
    {
        public static ExchangeOrder CreateOrder(long id, OrderStatus filled = OrderStatus.Filled, decimal price = 0, decimal executingQuantity = 0, decimal originalQuantity = 0, decimal cumulativeQuoteQuantity = 0)
        {
            return new ExchangeOrder
            {
                ExchangeId = "Test",
                OrderId = id.ToString(),
                ClientOrderId = string.Empty,
                Symbol = "BTC_USDT",
                Side = ExchangeOrderSide.Buy,
                Type = ExchangeOrderType.Market,
                Status = MapStatus(filled),
                Price = price,
                Quantity = originalQuantity,
                FilledQuantity = executingQuantity,
                QuoteQuantity = cumulativeQuoteQuantity,
                Timestamp = DateTime.Now
            };
        }

        private static ExchangeOrderStatus MapStatus(OrderStatus status)
        {
            return status switch
            {
                OrderStatus.New => ExchangeOrderStatus.New,
                OrderStatus.PartiallyFilled => ExchangeOrderStatus.PartiallyFilled,
                OrderStatus.Filled => ExchangeOrderStatus.Filled,
                OrderStatus.Canceled or OrderStatus.PendingCancel => ExchangeOrderStatus.Cancelled,
                OrderStatus.Rejected => ExchangeOrderStatus.Rejected,
                OrderStatus.Expired => ExchangeOrderStatus.Expired,
                _ => ExchangeOrderStatus.New,
            };
        }
    }

    class CandleStickHelper
    {
        public DateTime CloseTime { get; internal set; }
        public int Open { get; internal set; }
        public int High { get; internal set; }
        public int Low { get; internal set; }
        public int Close { get; internal set; }
        public int Volume { get; internal set; }

        public static ExchangeCandlestick CreateCandlestick(DateTime closeTime, decimal open, decimal high, decimal low, decimal close, decimal volume)
        {
            return new ExchangeCandlestick
            {
                Symbol = "BTC_USDT",
                Interval = CandleInterval.Minute_1,
                OpenTime = closeTime.AddMinutes(-1),
                Open = open,
                High = high,
                Low = low,
                Close = close,
                Volume = volume,
                CloseTime = closeTime,
                QuoteVolume = 0m,
                NumberOfTrades = 0,
                IsClosed = true,
            };
        }
    }
}
