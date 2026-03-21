using CryptoTrading.App.Core;
using CryptoTrading.App.Core.KeyClass;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Monitor;
using Microsoft.Extensions.Logging;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Moq;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;

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
            // Act
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            // Assert
            Assert.IsNotNull(processor);
            Assert.IsNotNull(processor.OrderMonitors);
            Assert.IsEmpty(processor.OrderMonitors);
            Assert.AreEqual("1", processor.KeyValue);
        }

        [TestMethod]
        public void Constructor_WithPositions_SetsPositionsProperty()
        {
            // Act
            var processor = new TradeProcessor(_mockLogger.Object, _mockPositions.Object, _mockFactory.Object);

            // Assert
            Assert.AreEqual(_mockPositions.Object, processor.Positions);
        }

        [TestMethod]
        public void Constructor_WithKey_SetsKeyValue()
        {
            // Arrange
            _mockKey.Setup(k => k.KeyValue).Returns("TestKey123");

            // Act
            var processor = new TradeProcessor(_mockPositions.Object, _mockLogger.Object, _mockFactory.Object, _mockKey.Object);

            // Assert
            Assert.AreEqual("TestKey123", processor.KeyValue);
        }

        [TestMethod]
        public void CurrentMonitors_ReturnsOnlyLiveMonitors()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);
            var mockLiveMonitor = new Mock<ITradeMonitor>();
            mockLiveMonitor.Setup(m => m.Live).Returns(true);

            var mockInactiveMonitor = new Mock<ITradeMonitor>();
            mockInactiveMonitor.Setup(m => m.Live).Returns(false);

            processor.OrderMonitors.Add(mockLiveMonitor.Object);
            processor.OrderMonitors.Add(mockInactiveMonitor.Object);

            // Act
            var currentMonitors = processor.CurrentMonitors.ToList();

            // Assert
            Assert.HasCount(1, currentMonitors);
            Assert.Contains(mockLiveMonitor.Object, currentMonitors);
            Assert.DoesNotContain(mockInactiveMonitor.Object, currentMonitors);
        }

        [TestMethod]
        public void LiveTrades_ReturnsLastTradeFromLiveMonitors()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            var mockTrade1 = new Mock<ITrade>();
            var mockTrade2 = new Mock<ITrade>();

            var mockMonitor = new Mock<ITradeMonitor>();
            mockMonitor.Setup(m => m.Live).Returns(true);
            mockMonitor.Setup(m => m.HistoricTrades).Returns(new List<ITrade> { mockTrade1.Object, mockTrade2.Object });

            processor.OrderMonitors.Add(mockMonitor.Object);

            // Act
            var liveTrades = processor.LiveTrades.ToList();

            // Assert
            Assert.HasCount(1, liveTrades);
            Assert.AreEqual(mockTrade2.Object, liveTrades.First());
        }

        [TestMethod]
        public void LiveTrades_WithMultipleLiveMonitors_ReturnsLastTradeFromEach()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            var mockTrade1 = new Mock<ITrade>();
            var mockTrade2 = new Mock<ITrade>();
            var mockTrade3 = new Mock<ITrade>();

            var mockMonitor1 = new Mock<ITradeMonitor>();
            mockMonitor1.Setup(m => m.Live).Returns(true);
            mockMonitor1.Setup(m => m.HistoricTrades).Returns(new List<ITrade> { mockTrade1.Object, mockTrade2.Object });

            var mockMonitor2 = new Mock<ITradeMonitor>();
            mockMonitor2.Setup(m => m.Live).Returns(true);
            mockMonitor2.Setup(m => m.HistoricTrades).Returns(new List<ITrade> { mockTrade3.Object });

            processor.OrderMonitors.Add(mockMonitor1.Object);
            processor.OrderMonitors.Add(mockMonitor2.Object);

            // Act
            var liveTrades = processor.LiveTrades.ToList();

            // Assert
            Assert.HasCount(2, liveTrades);
            Assert.Contains(mockTrade2.Object, liveTrades);
            Assert.Contains(mockTrade3.Object, liveTrades);
        }

        [TestMethod]
        public void LiveTrades_WithNoLiveMonitors_ReturnsEmptyCollection()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            var mockMonitor = new Mock<ITradeMonitor>();
            mockMonitor.Setup(m => m.Live).Returns(false);

            processor.OrderMonitors.Add(mockMonitor.Object);

            // Act
            var liveTrades = processor.LiveTrades.ToList();

            // Assert
            Assert.IsEmpty(liveTrades);
        }

        [TestMethod]
        public void CompleteAllTransactions_CallsCompleteTradeOnAllMonitors()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);
            var mockMonitor1 = new Mock<ITradeMonitor>();
            var mockMonitor2 = new Mock<ITradeMonitor>();

            processor.OrderMonitors.Add(mockMonitor1.Object);
            processor.OrderMonitors.Add(mockMonitor2.Object);

            // Act
            processor.CompleteAllTransactions();

            // Assert
            mockMonitor1.Verify(m => m.CompleteTrade(), Times.Once);
            mockMonitor2.Verify(m => m.CompleteTrade(), Times.Once);
        }

        [TestMethod]
        public void CompleteAllTransactions_WithNoMonitors_DoesNotThrow()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            // Act & Assert
            processor.CompleteAllTransactions(); // Should not throw
        }

        [TestMethod]
        public void ClearInactiveTrades_RemovesOnlyInactiveMonitors()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            var mockLiveMonitor = new Mock<ITradeMonitor>();
            mockLiveMonitor.Setup(m => m.Live).Returns(true);

            var mockInactiveMonitor = new Mock<ITradeMonitor>();
            mockInactiveMonitor.Setup(m => m.Live).Returns(false);

            processor.OrderMonitors.Add(mockLiveMonitor.Object);
            processor.OrderMonitors.Add(mockInactiveMonitor.Object);

            // Act
            processor.ClearInactiveTrades();

            // Assert
            Assert.HasCount(1, processor.OrderMonitors);
            Assert.Contains(mockLiveMonitor.Object, processor.OrderMonitors);
            Assert.DoesNotContain(mockInactiveMonitor.Object, processor.OrderMonitors);
        }

        [TestMethod]
        public void ClearInactiveTrades_WithAllLiveMonitors_RemovesNothing()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            var mockLiveMonitor1 = new Mock<ITradeMonitor>();
            mockLiveMonitor1.Setup(m => m.Live).Returns(true);

            var mockLiveMonitor2 = new Mock<ITradeMonitor>();
            mockLiveMonitor2.Setup(m => m.Live).Returns(true);

            processor.OrderMonitors.Add(mockLiveMonitor1.Object);
            processor.OrderMonitors.Add(mockLiveMonitor2.Object);

            // Act
            processor.ClearInactiveTrades();

            // Assert
            Assert.HasCount(2, processor.OrderMonitors);
        }

        [TestMethod]
        public void GetCompletedTrades_ReturnsAllHistoricTrades()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            var mockTrade1 = new Mock<ITrade>();
            var mockTrade2 = new Mock<ITrade>();
            var mockTrade3 = new Mock<ITrade>();

            var mockMonitor1 = new Mock<ITradeMonitor>();
            mockMonitor1.Setup(m => m.HistoricTrades).Returns(new List<ITrade> { mockTrade1.Object, mockTrade2.Object });

            var mockMonitor2 = new Mock<ITradeMonitor>();
            mockMonitor2.Setup(m => m.HistoricTrades).Returns(new List<ITrade> { mockTrade3.Object });

            processor.OrderMonitors.Add(mockMonitor1.Object);
            processor.OrderMonitors.Add(mockMonitor2.Object);

            // Act
            var completedTrades = processor.GetCompletedTrades();

            // Assert
            Assert.HasCount(3, completedTrades);
            Assert.Contains(mockTrade1.Object, completedTrades);
            Assert.Contains(mockTrade2.Object, completedTrades);
            Assert.Contains(mockTrade3.Object, completedTrades);
        }

        [TestMethod]
        public void GetCompletedTrades_WithNoMonitors_ReturnsEmptyList()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            // Act
            var completedTrades = processor.GetCompletedTrades();

            // Assert
            Assert.IsNotNull(completedTrades);
            Assert.IsEmpty(completedTrades);
        }

        [TestMethod]
        public void Configure_SetsConfigProperty()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);
            var mockConfig = new Mock<IConfig>();

            // Act
            processor.Configure(mockConfig.Object);

            // Assert
            Assert.AreEqual(mockConfig.Object, processor.Config);
        }

        [TestMethod]
        public void Configure_WithNull_SetsConfigToNull()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            // Act
            processor.Configure(null);

            // Assert
            Assert.IsNull(processor.Config);
        }

        [TestMethod]
        public void OrderMonitors_CanAddAndRetrieveMonitors()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);
            var mockMonitor = new Mock<ITradeMonitor>();

            // Act
            processor.OrderMonitors.Add(mockMonitor.Object);

            // Assert
            Assert.Contains(mockMonitor.Object, processor.OrderMonitors);
            Assert.HasCount(1, processor.OrderMonitors);
        }

        [TestMethod]
        public void OrderMonitors_CanAddMultipleMonitors()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);
            var mockMonitor1 = new Mock<ITradeMonitor>();
            var mockMonitor2 = new Mock<ITradeMonitor>();
            var mockMonitor3 = new Mock<ITradeMonitor>();

            // Act
            processor.OrderMonitors.Add(mockMonitor1.Object);
            processor.OrderMonitors.Add(mockMonitor2.Object);
            processor.OrderMonitors.Add(mockMonitor3.Object);

            // Assert
            Assert.HasCount(3, processor.OrderMonitors);
        }

        [TestMethod]
        public void CurrentMonitors_WithNoMonitors_ReturnsEmptyCollection()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            // Act
            var currentMonitors = processor.CurrentMonitors.ToList();

            // Assert
            Assert.IsEmpty(currentMonitors);
        }

        [TestMethod]
        public void KeyValue_DefaultsToOne_WhenNotSpecified()
        {
            // Act
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            // Assert
            Assert.AreEqual("1", processor.KeyValue);
        }

        [TestMethod]
        public void KeyValue_CanBeSet()
        {
            // Arrange
            var processor = new TradeProcessor(_mockLogger.Object, _mockFactory.Object);

            // Act
            processor.KeyValue = "CustomKey";

            // Assert
            Assert.AreEqual("CustomKey", processor.KeyValue);
        }
    }
}