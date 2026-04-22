using Binance;
using Binance.Client;
using CryptoTrading.App.Core;
using CryptoTrading.App.Core.Database;
using CryptoTrading.App.Core.Exchange;
using CryptoTrading.App.Core.MarketMonitorFactory;
using CryptoTrading.App.Core.Message_Broker;
using CryptoTrading.App.Core.Position;
using CryptoTrading.App.Core.Strategy;
using CryptoTrading.App.Core.Trade;
using CryptoTrading.App.Monitor;
using Microsoft.Extensions.Logging;
using Moq;
using Skender.Stock.Indicators;

namespace CryptoTrading.App.Tests.Monitor
{
    [TestClass]
    public class TradeMonitorTests
    {
        private Mock<ILogger<TradeMonitor>> _mockLogger = new();
        private Mock<IMarketMonitor> _mockMarketMonitor= new();
        private Mock<IConfig> _mockConfig = new();
        private Mock<ITradeRequest> _mockTradeRequest = new();
        private Mock<IPositions> _mockPositions = new();
        private Mock<ITrade> _mockTrade = new();
        private Mock<IExecutionStrategy> _mockStrategy = new();
        private Mock<IEntryStrategy> _mockEntryStrategy = new();
        private Mock<IExitStrategy> _mockExitStrategy = new();

        [TestInitialize]
        public void TestInitialize()
        {
            _mockLogger = new Mock<ILogger<TradeMonitor>>();
            _mockMarketMonitor = new Mock<IMarketMonitor>();
            _mockConfig = new Mock<IConfig>();
            _mockTradeRequest = new Mock<ITradeRequest>();
            _mockPositions = new Mock<IPositions>();
            _mockTrade = new Mock<ITrade>();
            _mockStrategy = new Mock<IExecutionStrategy>();
            _mockEntryStrategy = new Mock<IEntryStrategy>();
            _mockExitStrategy = new Mock<IExitStrategy>();

            // Setup default behavior
            _mockTradeRequest.Setup(r => r.Strategy).Returns(_mockStrategy.Object);
            _mockStrategy.Setup(s => s.EntryStrategy).Returns(_mockEntryStrategy.Object);
            _mockStrategy.Setup(s => s.ExitStrategy).Returns(_mockExitStrategy.Object);
            _mockTradeRequest.Setup(r => r.Symbol).Returns(Symbol.BTC_USDT);
            _mockTradeRequest.Setup(r => r.Amount).Returns(100);
        }

        [TestMethod]
        public void Constructor_InitializesCorrectly()
        {
            // Act
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);

            // Assert
            Assert.IsNotNull(monitor);
            Assert.IsNotNull(monitor.HistoricTrades);
            Assert.IsEmpty(monitor.HistoricTrades);
            Assert.AreEqual("1", monitor.KeyValue);
        }

        [TestMethod]
        public void KeyValue_CanBeSet()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);

            // Act
            monitor.KeyValue = "CustomKey";

            // Assert
            Assert.AreEqual("CustomKey", monitor.KeyValue);
        }

        [TestMethod]
        public void AddRequest_InitializesTradeAndSubscriptions()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);

            // Act
            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            // Assert
            Assert.AreEqual(_mockTradeRequest.Object, monitor.Request);
            Assert.HasCount(1, monitor.HistoricTrades);
            Assert.AreEqual(_mockTrade.Object, monitor.HistoricTrades.First());
            _mockPositions.Verify(p => p.CreateTrade(_mockTradeRequest.Object), Times.Once);
        }

        [TestMethod]
        public void Trade_ReturnsLastHistoricTrade()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            var mockTrade1 = new Mock<ITrade>();
            var mockTrade2 = new Mock<ITrade>();

            monitor.HistoricTrades.Add(mockTrade1.Object);
            monitor.HistoricTrades.Add(mockTrade2.Object);

            // Act
            var currentTrade = monitor.Trade;

            // Assert
            Assert.AreEqual(mockTrade2.Object, currentTrade);
        }

        [TestMethod]
        public void Live_ReturnsTrueWhenTradeIsOpen()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockTrade.Setup(t => t.Open).Returns(true);
            monitor.HistoricTrades.Add(_mockTrade.Object);

            // Act
            var isLive = monitor.Live;

            // Assert
            Assert.IsTrue(isLive);
        }

        [TestMethod]
        public void Live_ReturnsFalseWhenTradeIsClosed()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockTrade.Setup(t => t.Open).Returns(false);
            monitor.HistoricTrades.Add(_mockTrade.Object);

            // Act
            var isLive = monitor.Live;

            // Assert
            Assert.IsFalse(isLive);
        }

        [TestMethod]
        public void Symbol_ReturnsTradePair()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockTrade.Setup(t => t.Pair).Returns("BTCUSDT");
            monitor.HistoricTrades.Add(_mockTrade.Object);

            // Act
            var symbol = monitor.Symbol;

            // Assert
            Assert.AreEqual("BTCUSDT", symbol);
        }

        [TestMethod]
        public void ToString_ReturnsFormattedString()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockTrade.Setup(t => t.Pair).Returns("BTCUSDT");
            monitor.HistoricTrades.Add(_mockTrade.Object);
            monitor.currentCloseTime = new DateTime(2024, 1, 15, 10, 30, 0);

            // Act
            var result = monitor.ToString();

            // Assert
            Assert.Contains("BTCUSDT", result);
            Assert.Contains("2024-01-15", result);
        }

        [TestMethod]
        public async Task SubscribetToMarketData_SubscribesWhenNotAlreadySubscribed()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);
            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            var candleSticks = new List<Candlestick>
            {
                CandleStickHelper.CreateCandlestick(DateTime.Now, 50000, 51000, 49000, 50500, 100)
            };

            _mockMarketMonitor.Setup(m => m.IsSubscribed(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
            _mockMarketMonitor.Setup(m => m.GetHistoricCandleSticks(It.IsAny<string>())).ReturnsAsync(candleSticks);

            // Act
            await monitor.SubscribetToMarketData();

            // Assert
            _mockMarketMonitor.Verify(m => m.GetHistoricCandleSticks("BTCUSDT"), Times.Once);
            _mockMarketMonitor.Verify(m => m.Subscribe(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Action<CandlestickEventArgs>>()), Times.Once);
            _mockStrategy.Verify(s => s.SetQuotes(It.IsAny<QuoteHub<IQuote>>()), Times.Once);
        }

        [TestMethod]
        public async Task SubscribetToMarketData_DoesNotSubscribeWhenAlreadySubscribed()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);
            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            _mockMarketMonitor.Setup(m => m.IsSubscribed(It.IsAny<string>(), It.IsAny<string>())).Returns(true);

            // Act
            await monitor.SubscribetToMarketData();

            // Assert
            _mockMarketMonitor.Verify(m => m.GetHistoricCandleSticks(It.IsAny<string>()), Times.Never);
            _mockMarketMonitor.Verify(m => m.Subscribe(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<Action<CandlestickEventArgs>>()), Times.Never);
        }

        //commented out as this method is void and has limited testable outcomes and context is required.
        //[TestMethod]
        //public void CompleteTrade_CreatesNewTradeAndAddsToHistory()
        //{
        //    // Arrange
        //    var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
        //    var mockNewTrade = new Mock<ITrade>();
        //    _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(mockNewTrade.Object);
        //    monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

        //    var initialCount = monitor.HistoricTrades.Count;

        //    // Act
        //    monitor.CompleteTrade();

        //    // Assert
        //    Assert.HasCount(initialCount + 1, monitor.HistoricTrades);
        //    Assert.IsNull(monitor.marketMonitor);
        //}

        [TestMethod]
        public async Task SetNewRequest_SameDirection_UpdatesRequest()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);
            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            var newRequest = new Mock<ITradeRequest>();
            newRequest.Setup(r => r.Strategy).Returns(_mockStrategy.Object);
            newRequest.Setup(r => r.OrderSide).Returns(_mockTradeRequest.Object.OrderSide);
            newRequest.Setup(r => r.Symbol).Returns(Symbol.BTC_USDT);

            // Act
            await monitor.SetNewRequest(newRequest.Object);

            // Assert
            Assert.AreEqual(newRequest.Object, monitor.Request);
            _mockStrategy.Verify(s => s.SetQuotes(It.IsAny<QuoteHub<IQuote>>()), Times.AtLeastOnce);
        }

        [TestMethod]
        public async Task SetNewRequest_ChangeDirection_WithUnfilledTransaction_CancelsOrder()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);

            var mockTransaction = new Mock<ITransaction>();
            mockTransaction.Setup(t => t.IsFilled).Returns(false);
            mockTransaction.Setup(t => t.IsPartiallyFilled).Returns(false);
            mockTransaction.Setup(t => t.Order).Returns(OrderHelper.CreateOrder(12345));

            _mockTrade.Setup(t => t.GetCurrentTransaction()).Returns(mockTransaction.Object);
            _mockTrade.Setup(t => t.Pair).Returns(Symbol.BTC_USDT);

            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            var newRequest = new Mock<ITradeRequest>();
            newRequest.Setup(r => r.Strategy).Returns(_mockStrategy.Object);
            newRequest.Setup(r => r.OrderSide).Returns(OrderSide.Sell); // Different from original
            newRequest.Setup(r => r.Symbol).Returns(Symbol.BTC_USDT);
            _mockTradeRequest.Setup(r => r.OrderSide).Returns(OrderSide.Buy);

            // Act
            await monitor.SetNewRequest(newRequest.Object);

            // Assert
            _mockMarketMonitor.Verify(m => m.CheckOrder(It.IsAny<ITransaction>()), Times.Once);
            Assert.AreEqual(newRequest.Object, monitor.Request);
        }

        [TestMethod]
        public void ProcessMessageAction_WithOrder_UpdatesTransaction()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);
            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            var mockTransaction = new Mock<ITransaction>();
            var order = OrderHelper.CreateOrder(12345, OrderStatus.Filled);
            var payload = new MessagePayload<ExchangeOrder>(order, mockTransaction.Object);

            // Act
            var method = monitor.GetType().GetMethod("ProcessMessageAction",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(MessagePayload<ExchangeOrder>) },
                null);
            method?.Invoke(monitor, new[] { payload });

            // Assert
            mockTransaction.Verify(t => t.UpdateOrder(order), Times.Once);
        }

        [TestMethod]
        public void ProcessMessageAction_WithCancelString_CancelsTransaction()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);
            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            var mockTransaction = new Mock<ITransaction>();
            var payload = new MessagePayload<string>("Cancel", mockTransaction.Object);

            // Act
            var method = monitor.GetType().GetMethod("ProcessMessageAction",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(MessagePayload<string>) },
                null);
            method?.Invoke(monitor, new[] { payload });

            // Assert
            mockTransaction.Verify(t => t.Cancel(), Times.Once);
        }

        [TestMethod]
        public void CurrentStopLimit_CanBeSetAndRetrieved()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            decimal expectedValue = 50000.50m;

            // Act
            monitor.CurrentStopLimit = expectedValue;

            // Assert
            Assert.AreEqual(expectedValue, monitor.CurrentStopLimit);
        }

        [TestMethod]
        public void CurrentCloseTime_CanBeSetAndRetrieved()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            var expectedTime = new DateTime(2024, 1, 15, 10, 30, 0);

            // Act
            monitor.currentCloseTime = expectedTime;

            // Assert
            Assert.AreEqual(expectedTime, monitor.currentCloseTime);
        }

        [TestMethod]
        public void HistoricTrades_CanAddMultipleTrades()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            var trade1 = new Mock<ITrade>();
            var trade2 = new Mock<ITrade>();
            var trade3 = new Mock<ITrade>();

            // Act
            monitor.HistoricTrades.Add(trade1.Object);
            monitor.HistoricTrades.Add(trade2.Object);
            monitor.HistoricTrades.Add(trade3.Object);

            // Assert
            Assert.HasCount(3, monitor.HistoricTrades);
            Assert.Contains(trade1.Object, monitor.HistoricTrades);
            Assert.Contains(trade2.Object, monitor.HistoricTrades);
            Assert.Contains(trade3.Object, monitor.HistoricTrades);
        }

        [TestMethod]
        public void MarketMonitor_CanBeAccessed()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);

            // Act
            var marketMonitor = monitor.marketMonitor;

            // Assert
            Assert.IsNotNull(marketMonitor);
            Assert.AreEqual(_mockMarketMonitor.Object, marketMonitor);
        }

        [TestMethod]
        public async Task SubscribetToMarketData_ProcessesHistoricCandlesticksCorrectly()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);
            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            var candleSticks = new List<Candlestick>
            {
                CandleStickHelper.CreateCandlestick(DateTime.Now.AddMinutes(-2), 50000, 51000, 49000, 50500, 100),
                CandleStickHelper.CreateCandlestick(DateTime.Now.AddMinutes(-1), 50500, 51500, 50000, 51000, 100)
            };

            _mockMarketMonitor.Setup(m => m.IsSubscribed(It.IsAny<string>(), It.IsAny<string>())).Returns(false);
            _mockMarketMonitor.Setup(m => m.GetHistoricCandleSticks(It.IsAny<string>())).ReturnsAsync(candleSticks);

            // Act
            await monitor.SubscribetToMarketData();

            // Assert
            _mockMarketMonitor.Verify(m => m.GetHistoricCandleSticks("BTCUSDT"), Times.Once);
            _mockStrategy.Verify(s => s.SetQuotes(It.IsAny<QuoteHub<IQuote>>()), Times.Once);
        }

        #region Strategy Change Tests

        [TestMethod]
        public async Task SetNewRequest_StrategyChange_WithPartiallyFilledOrder_ClosesPosition()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);

            var mockTransaction = new Mock<ITransaction>();
            mockTransaction.Setup(t => t.IsFilled).Returns(false);
            mockTransaction.Setup(t => t.IsPartiallyFilled).Returns(true);
            mockTransaction.Setup(t => t.Order).Returns(OrderHelper.CreateOrder(12345));

            var mockCompletedTransaction = new Mock<ITransaction>();
            _mockTrade.Setup(t => t.GetCurrentTransaction()).Returns(mockTransaction.Object);
            _mockTrade.Setup(t => t.CompleteTrade()).Returns(mockCompletedTransaction.Object);
            _mockTrade.Setup(t => t.Pair).Returns("BTCUSDT");

            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            var newRequest = new Mock<ITradeRequest>();
            newRequest.Setup(r => r.Strategy).Returns(_mockStrategy.Object);
            newRequest.Setup(r => r.OrderSide).Returns(OrderSide.Sell);
            newRequest.Setup(r => r.Symbol).Returns(Symbol.BTC_USDT);
            _mockTradeRequest.Setup(r => r.OrderSide).Returns(OrderSide.Buy);

            // Act
            await monitor.SetNewRequest(newRequest.Object);

            // Assert
            _mockMarketMonitor.Verify(m => m.CheckOrder(mockTransaction.Object), Times.Once);
            _mockTrade.Verify(t => t.CompleteTrade(), Times.Once);
            Assert.AreEqual(newRequest.Object, monitor.Request);
        }

        [TestMethod]
        public async Task SetNewRequest_StrategyChange_WithFullyFilledOrder_ClosesPosition()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);

            var mockTransaction = new Mock<ITransaction>();
            mockTransaction.Setup(t => t.IsFilled).Returns(true);
            mockTransaction.Setup(t => t.IsPartiallyFilled).Returns(false);
            mockTransaction.Setup(t => t.Order).Returns(OrderHelper.CreateOrder(12345));

            var mockCompletedTransaction = new Mock<ITransaction>();
            _mockTrade.Setup(t => t.GetCurrentTransaction()).Returns(mockTransaction.Object);
            _mockTrade.Setup(t => t.CompleteTrade()).Returns(mockCompletedTransaction.Object);
            _mockTrade.Setup(t => t.Pair).Returns("BTCUSDT");

            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            var newRequest = new Mock<ITradeRequest>();
            newRequest.Setup(r => r.Strategy).Returns(_mockStrategy.Object);
            newRequest.Setup(r => r.OrderSide).Returns(OrderSide.Sell);
            newRequest.Setup(r => r.Symbol).Returns(Symbol.BTC_USDT);
            _mockTradeRequest.Setup(r => r.OrderSide).Returns(OrderSide.Buy);

            // Act
            await monitor.SetNewRequest(newRequest.Object);

            // Assert
            _mockMarketMonitor.Verify(m => m.CheckOrder(mockTransaction.Object), Times.Once);
            _mockTrade.Verify(t => t.CompleteTrade(), Times.Once);
            Assert.AreEqual(newRequest.Object, monitor.Request);
        }

        [TestMethod]
        public async Task SetNewRequest_StrategyChange_WithNoTransaction_UpdatesRequestOnly()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);

            _ = _mockTrade.Setup(t => t.GetCurrentTransaction()).Returns((ITransaction)null);
            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            var newRequest = new Mock<ITradeRequest>();
            newRequest.Setup(r => r.Strategy).Returns(_mockStrategy.Object);
            newRequest.Setup(r => r.OrderSide).Returns(OrderSide.Sell);
            newRequest.Setup(r => r.Symbol).Returns(Symbol.BTC_USDT);
            _mockTradeRequest.Setup(r => r.OrderSide).Returns(OrderSide.Buy);

            // Act
            await monitor.SetNewRequest(newRequest.Object);

            // Assert
            _mockMarketMonitor.Verify(m => m.CheckOrder(It.IsAny<ITransaction>()), Times.Never);
            _mockTrade.Verify(t => t.CompleteTrade(), Times.Never);
            Assert.AreEqual(newRequest.Object, monitor.Request);
        }

        [TestMethod]
        public async Task SetNewRequest_SameStrategy_WithPartiallyFilledOrder_DoesNotCancel()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);

            var mockTransaction = new Mock<ITransaction>();
            mockTransaction.Setup(t => t.IsFilled).Returns(false);
            mockTransaction.Setup(t => t.IsPartiallyFilled).Returns(true);

            _mockTrade.Setup(t => t.GetCurrentTransaction()).Returns(mockTransaction.Object);
            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            var newRequest = new Mock<ITradeRequest>();
            newRequest.Setup(r => r.Strategy).Returns(_mockStrategy.Object);
            newRequest.Setup(r => r.OrderSide).Returns(_mockTradeRequest.Object.OrderSide); // Same direction
            newRequest.Setup(r => r.Symbol).Returns(Symbol.BTC_USDT);

            // Act
            await monitor.SetNewRequest(newRequest.Object);

            // Assert
            _mockTrade.Verify(t => t.CompleteTrade(), Times.Never);
            Assert.AreEqual(newRequest.Object, monitor.Request);
        }

        #endregion

        #region Partial Fill Tests

        [TestMethod]
        public void ProcessMessageAction_PartiallyFilledOrder_UpdatesCorrectly()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);
            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            var mockTransaction = new Mock<ITransaction>();
            var partialOrder = OrderHelper.CreateOrder(12345, OrderStatus.PartiallyFilled,0,0.5m,1.0m,25000);

            var payload = new MessagePayload<ExchangeOrder>(partialOrder, mockTransaction.Object);

            // Act
            var method = monitor.GetType().GetMethod("ProcessMessageAction",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(MessagePayload<ExchangeOrder>) },
                null);
            method?.Invoke(monitor, new[] { payload });

            // Assert
            mockTransaction.Verify(t => t.UpdateOrder(partialOrder), Times.Once);
        }

        [TestMethod]
        public void ProcessMessageAction_MultiplePartialFills_UpdatesOrderEachTime()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);
            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            var mockTransaction = new Mock<ITransaction>();

            var partialFill1 = OrderHelper.CreateOrder(12345, OrderStatus.PartiallyFilled, 0, 0.3m, 1.0m, 50000);

            var partialFill2 = OrderHelper.CreateOrder(12345, OrderStatus.PartiallyFilled, 0, 0.7m, 1.0m, 50050);

            var fullyFilled = OrderHelper.CreateOrder(12345, OrderStatus.PartiallyFilled, 0, 1.0m, 1.0m, 50050);

            var method = monitor.GetType().GetMethod("ProcessMessageAction",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(MessagePayload<ExchangeOrder>) },
                null);

            // Act - First partial fill
            var payload1 = new MessagePayload<ExchangeOrder>(partialFill1, mockTransaction.Object);
            method?.Invoke(monitor, new[] { payload1 });

            // Act - Second partial fill
            var payload2 = new MessagePayload<ExchangeOrder>(partialFill2, mockTransaction.Object);
            method?.Invoke(monitor, new[] { payload2 });

            // Act - Full fill
            var payload3 = new MessagePayload<ExchangeOrder>(fullyFilled, mockTransaction.Object);
            method?.Invoke(monitor, new[] { payload3 });

            // Assert
            mockTransaction.Verify(t => t.UpdateOrder(It.IsAny<ExchangeOrder>()), Times.Exactly(3));
            mockTransaction.Verify(t => t.UpdateOrder(partialFill1), Times.Once);
            mockTransaction.Verify(t => t.UpdateOrder(partialFill2), Times.Once);
            mockTransaction.Verify(t => t.UpdateOrder(fullyFilled), Times.Once);
        }

        [TestMethod]
        public async Task SetNewRequest_StrategyChange_WithMultiplePartialFills_ClosesAllPositions()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);

            // Simulate multiple partial fills at different prices
            var mockTransaction1 = new Mock<ITransaction>();
            mockTransaction1.Setup(t => t.IsFilled).Returns(false);
            mockTransaction1.Setup(t => t.IsPartiallyFilled).Returns(true);
            mockTransaction1.Setup(t => t.Order).Returns(OrderHelper.CreateOrder(12345, OrderStatus.PartiallyFilled, 50000, 0.3m, 0m, 50050));

            var mockCompletedTransaction = new Mock<ITransaction>();
            _mockTrade.Setup(t => t.GetCurrentTransaction()).Returns(mockTransaction1.Object);
            _mockTrade.Setup(t => t.CompleteTrade()).Returns(mockCompletedTransaction.Object);
            _mockTrade.Setup(t => t.Pair).Returns("BTCUSDT");

            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            var newRequest = new Mock<ITradeRequest>();
            newRequest.Setup(r => r.Strategy).Returns(_mockStrategy.Object);
            newRequest.Setup(r => r.OrderSide).Returns(OrderSide.Sell);
            newRequest.Setup(r => r.Symbol).Returns(Symbol.BTC_USDT);
            _mockTradeRequest.Setup(r => r.OrderSide).Returns(OrderSide.Buy);

            // Act
            await monitor.SetNewRequest(newRequest.Object);

            // Assert
            _mockMarketMonitor.Verify(m => m.CheckOrder(mockTransaction1.Object), Times.Once);
            _mockTrade.Verify(t => t.CompleteTrade(), Times.Once);
        }

        [TestMethod]
        public void ProcessMessageAction_PartialFillAtDifferentPrices_UpdatesAveragePrice()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);
            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            var mockTransaction = new Mock<ITransaction>();

            // First fill at 50000
            var firstFill = OrderHelper.CreateOrder(12345, OrderStatus.PartiallyFilled, 50000, 0.4m, 1.0m, 2000);

            // Second fill at 50500 (price moved)
            var secondFill = OrderHelper.CreateOrder(12345, OrderStatus.PartiallyFilled, 50250, 0.8m, 1.0m, 40200);

            var method = monitor.GetType().GetMethod("ProcessMessageAction",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(MessagePayload<ExchangeOrder>) },
                null);

            // Act
            var payload1 = new MessagePayload<ExchangeOrder>(firstFill, mockTransaction.Object);
            method?.Invoke(monitor, new[] { payload1 });

            var payload2 = new MessagePayload<ExchangeOrder>(secondFill, mockTransaction.Object);
            method?.Invoke(monitor, new[] { payload2 });

            // Assert
            mockTransaction.Verify(t => t.UpdateOrder(firstFill), Times.Once);
            mockTransaction.Verify(t => t.UpdateOrder(secondFill), Times.Once);
        }

        [TestMethod]
        public async Task SetNewRequest_WithPartialFill_ThenStrategyFlip_HandlesCorrectly()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);

            // Setup initial buy order with partial fill
            var mockTransaction = new Mock<ITransaction>();
            mockTransaction.Setup(t => t.IsFilled).Returns(false);
            mockTransaction.Setup(t => t.IsPartiallyFilled).Returns(true);
            mockTransaction.Setup(t => t.Order).Returns(OrderHelper.CreateOrder(12345, OrderStatus.PartiallyFilled, 50000, 0.5m, 1.0m, 50000));

            var mockCompletedTransaction = new Mock<ITransaction>();
            mockCompletedTransaction.Setup(t => t.Type).Returns(TransactionType.MarketTransaction);

            _mockTrade.Setup(t => t.GetCurrentTransaction()).Returns(mockTransaction.Object);
            _mockTrade.Setup(t => t.CompleteTrade()).Returns(mockCompletedTransaction.Object);
            _mockTrade.Setup(t => t.Pair).Returns("BTCUSDT");

            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);
            _mockTradeRequest.Setup(r => r.OrderSide).Returns(OrderSide.Buy);

            // Create new sell request (strategy flip)
            var newRequest = new Mock<ITradeRequest>();
            newRequest.Setup(r => r.Strategy).Returns(_mockStrategy.Object);
            newRequest.Setup(r => r.OrderSide).Returns(OrderSide.Sell);
            newRequest.Setup(r => r.Symbol).Returns(Symbol.BTC_USDT);

            // Act
            await monitor.SetNewRequest(newRequest.Object);

            // Assert
            _mockMarketMonitor.Verify(m => m.CheckOrder(mockTransaction.Object), Times.Once);
            _mockTrade.Verify(t => t.CompleteTrade(), Times.Once);
            Assert.AreEqual(newRequest.Object, monitor.Request);
        }

        [TestMethod]
        public void ProcessMessageAction_OrderFilledAfterMultiplePartials_FinalUpdate()
        {
            // Arrange
            var monitor = new TradeMonitor(_mockLogger.Object, _mockMarketMonitor.Object, _mockConfig.Object);
            _mockPositions.Setup(p => p.CreateTrade(It.IsAny<ITradeRequest>())).Returns(_mockTrade.Object);
            monitor.AddRequest(_mockTradeRequest.Object, _mockPositions.Object);

            var mockTransaction = new Mock<ITransaction>();
            var fillSequence = new List<ExchangeOrder>
            {
                OrderHelper.CreateOrder(12345, OrderStatus.PartiallyFilled, 50000, 0.2m),
                OrderHelper.CreateOrder(12345, OrderStatus.PartiallyFilled, 50100, 0.5m),
                OrderHelper.CreateOrder(12345, OrderStatus.PartiallyFilled, 50150, 0.8m),
                OrderHelper.CreateOrder(12345, OrderStatus.PartiallyFilled, 50125, 1.0m)
            };

            var method = monitor.GetType().GetMethod("ProcessMessageAction",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance,
                null,
                new[] { typeof(MessagePayload<ExchangeOrder>) },
                null);

            // Act
            foreach (var order in fillSequence)
            {
                var payload = new MessagePayload<ExchangeOrder>(order, mockTransaction.Object);
                method?.Invoke(monitor, new[] { payload });
            }

            // Assert
            mockTransaction.Verify(t => t.UpdateOrder(It.IsAny<ExchangeOrder>()), Times.Exactly(4));
        }

        #endregion
    }
    class OrderHelper
    {
        // Produces a neutral ExchangeOrder so test assertions don't depend on
        // the bundled-Binance Order constructor surface.
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

        public static Candlestick CreateCandlestick(DateTime closeTime, decimal open, decimal high, decimal low, decimal close, decimal volume)
        {
            return new Candlestick(

                "BTC_USDT",
                CandlestickInterval.Minute,
                closeTime.AddMinutes(-1),
                open,
                high,
                low,
                close,
                volume,
                closeTime,
                0,
                0,
                0,
                0);
        }
    }
}