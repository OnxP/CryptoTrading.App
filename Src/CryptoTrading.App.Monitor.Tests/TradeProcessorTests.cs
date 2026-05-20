using CryptoTrading.App.Core;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Broker.Position;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Monitor;
using CryptoTrading.App.Monitor.Position;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Tests.Monitor
{
    [TestClass]
    public class TradeProcessorTests
    {
        private Mock<ILogger<TradeProcessor>> _mockLogger = new();
        private Mock<IMarketMonitorFactory> _mockFactory= new();
        private Mock<IPositions> _mockPositions = new();
        private Mock<IKey> _mockKey = new();

        [TestInitialize]
        public void TestInitialize()
        {
            _mockLogger = new Mock<ILogger<TradeProcessor>>();
            _mockFactory = new Mock<IMarketMonitorFactory>();
            _mockPositions = new Mock<IPositions>();
            _mockKey = new Mock<IKey>();
        }

        [TestMethod]
        public void Constructor_WithLoggerAndFactory_InitializesCorrectly()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            Assert.IsNotNull(processor);
            Assert.IsNotNull(processor.OrderMonitors);
            Assert.IsEmpty(processor.OrderMonitors);
            Assert.AreEqual("1", processor.KeyValue);
        }

        [TestMethod]
        public void Constructor_WithPositions_SetsPositionsProperty()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockPositions.Object, _mockFactory.Object);

            Assert.AreEqual(_mockPositions.Object, processor.Positions);
        }

        [TestMethod]
        public void Constructor_WithKey_SetsKeyValue()
        {
            _mockKey.Setup(k => k.KeyValue).Returns("TestKey123");

            var processor = new TradeProcessor(_mockPositions.Object, _mockLogger.Object, _mockFactory.Object, _mockKey.Object);

            Assert.AreEqual("TestKey123", processor.KeyValue);
        }

        [TestMethod]
        public void CurrentMonitors_ReturnsOnlyLiveMonitors()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);
            var mockLiveMonitor = new Mock<ITradeMonitor>();
            mockLiveMonitor.Setup(m => m.Live).Returns(true);

            var mockInactiveMonitor = new Mock<ITradeMonitor>();
            mockInactiveMonitor.Setup(m => m.Live).Returns(false);

            processor.OrderMonitors.Add(mockLiveMonitor.Object);
            processor.OrderMonitors.Add(mockInactiveMonitor.Object);

            var currentMonitors = processor.CurrentMonitors.ToList();

            Assert.HasCount(1, currentMonitors);
            Assert.Contains(mockLiveMonitor.Object, currentMonitors);
            Assert.DoesNotContain(mockInactiveMonitor.Object, currentMonitors);
        }

        [TestMethod]
        public void CompleteAllTransactions_CallsCompleteTradeOnAllMonitors()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);
            var mockMonitor1 = new Mock<ITradeMonitor>();
            var mockMonitor2 = new Mock<ITradeMonitor>();

            processor.OrderMonitors.Add(mockMonitor1.Object);
            processor.OrderMonitors.Add(mockMonitor2.Object);

            processor.CompleteAllTransactions();

            mockMonitor1.Verify(m => m.CompleteTrade(), Times.Once);
            mockMonitor2.Verify(m => m.CompleteTrade(), Times.Once);
        }

        [TestMethod]
        public void CompleteAllTransactions_WithNoMonitors_DoesNotThrow()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            processor.CompleteAllTransactions();
        }

        [TestMethod]
        public void ClearInactiveTrades_RemovesOnlyInactiveMonitors()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            var mockLiveMonitor = new Mock<ITradeMonitor>();
            mockLiveMonitor.Setup(m => m.Live).Returns(true);

            var mockInactiveMonitor = new Mock<ITradeMonitor>();
            mockInactiveMonitor.Setup(m => m.Live).Returns(false);

            processor.OrderMonitors.Add(mockLiveMonitor.Object);
            processor.OrderMonitors.Add(mockInactiveMonitor.Object);

            processor.ClearInactiveTrades();

            Assert.HasCount(1, processor.OrderMonitors);
            Assert.Contains(mockLiveMonitor.Object, processor.OrderMonitors);
            Assert.DoesNotContain(mockInactiveMonitor.Object, processor.OrderMonitors);
        }

        [TestMethod]
        public void ClearInactiveTrades_WithAllLiveMonitors_RemovesNothing()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            var mockLiveMonitor1 = new Mock<ITradeMonitor>();
            mockLiveMonitor1.Setup(m => m.Live).Returns(true);

            var mockLiveMonitor2 = new Mock<ITradeMonitor>();
            mockLiveMonitor2.Setup(m => m.Live).Returns(true);

            processor.OrderMonitors.Add(mockLiveMonitor1.Object);
            processor.OrderMonitors.Add(mockLiveMonitor2.Object);

            processor.ClearInactiveTrades();

            Assert.HasCount(2, processor.OrderMonitors);
        }

        [TestMethod]
        public void GetCompletedTrades_ReturnsAllCompletedTrades()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            var record1 = new HistoricTradeRecord { Symbol = "BTCUSDT", ProfitPct = 5m };
            var record2 = new HistoricTradeRecord { Symbol = "ETHUSDT", ProfitPct = -2m };
            var record3 = new HistoricTradeRecord { Symbol = "SOLUSDT", ProfitPct = 10m };

            var mockMonitor1 = new Mock<ITradeMonitor>();
            mockMonitor1.Setup(m => m.CompletedTrades).Returns(new List<HistoricTradeRecord> { record1, record2 });

            var mockMonitor2 = new Mock<ITradeMonitor>();
            mockMonitor2.Setup(m => m.CompletedTrades).Returns(new List<HistoricTradeRecord> { record3 });

            processor.OrderMonitors.Add(mockMonitor1.Object);
            processor.OrderMonitors.Add(mockMonitor2.Object);

            var completedTrades = processor.GetCompletedTrades();

            Assert.HasCount(3, completedTrades);
        }

        [TestMethod]
        public void GetCompletedTrades_WithNoMonitors_ReturnsEmptyList()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            var completedTrades = processor.GetCompletedTrades();

            Assert.IsNotNull(completedTrades);
            Assert.IsEmpty(completedTrades);
        }

        [TestMethod]
        public void Configure_SetsConfigProperty()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);
            var mockConfig = new Mock<IConfig>();

            processor.Configure(mockConfig.Object);

            Assert.AreEqual(mockConfig.Object, processor.Config);
        }

        [TestMethod]
        public void Configure_WithNull_SetsConfigToNull()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            processor.Configure(null);

            Assert.IsNull(processor.Config);
        }

        [TestMethod]
        public void OrderMonitors_CanAddAndRetrieveMonitors()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);
            var mockMonitor = new Mock<ITradeMonitor>();

            processor.OrderMonitors.Add(mockMonitor.Object);

            Assert.Contains(mockMonitor.Object, processor.OrderMonitors);
            Assert.HasCount(1, processor.OrderMonitors);
        }

        [TestMethod]
        public void OrderMonitors_CanAddMultipleMonitors()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);
            var mockMonitor1 = new Mock<ITradeMonitor>();
            var mockMonitor2 = new Mock<ITradeMonitor>();
            var mockMonitor3 = new Mock<ITradeMonitor>();

            processor.OrderMonitors.Add(mockMonitor1.Object);
            processor.OrderMonitors.Add(mockMonitor2.Object);
            processor.OrderMonitors.Add(mockMonitor3.Object);

            Assert.HasCount(3, processor.OrderMonitors);
        }

        [TestMethod]
        public void CurrentMonitors_WithNoMonitors_ReturnsEmptyCollection()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            var currentMonitors = processor.CurrentMonitors.ToList();

            Assert.IsEmpty(currentMonitors);
        }

        [TestMethod]
        public void KeyValue_DefaultsToOne_WhenNotSpecified()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            Assert.AreEqual("1", processor.KeyValue);
        }

        [TestMethod]
        public void KeyValue_CanBeSet()
        {
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            processor.KeyValue = "CustomKey";

            Assert.AreEqual("CustomKey", processor.KeyValue);
        }
    }
}
