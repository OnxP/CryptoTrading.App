# CryptoTrading.App

A comprehensive algorithmic cryptocurrency trading application for the Binance exchange, built with .NET.

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)
![License](https://img.shields.io/badge/license-MIT-blue)
![Platform](https://img.shields.io/badge/platform-Binance-F0B90B)

## 📋 Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Features](#features)
- [Project Structure](#project-structure)
- [Getting Started](#getting-started)
- [Configuration](#configuration)
- [Trading Strategies](#trading-strategies)
- [Technical Indicators](#technical-indicators)
- [API Reference](#api-reference)
- [Testing](#testing)
- [Contributing](#contributing)
- [Disclaimer](#disclaimer)

## 🎯 Overview

CryptoTrading.App is a modular, extensible algorithmic trading platform designed for automated cryptocurrency trading on the Binance exchange. It supports:

- **Live Trading**: Real-time order execution via Binance WebSocket streams
- **Backtesting**: Historical data analysis with comprehensive metrics
- **Multiple Strategies**: 30+ pre-built trading strategies
- **Risk Management**: Configurable stop-loss and take-profit mechanisms
- **Multi-Timeframe Analysis**: Support for various candlestick intervals

## 🏗️ Architecture

The application follows a clean, layered architecture with dependency injection throughout:

```
┌─────────────────────────────────────────────────────────────────┐
│                    Application Layer                             │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
│  │ Console App │  │ Backtesting │  │ Algorithm Testing       │  │
│  └─────────────┘  └─────────────┘  └─────────────────────────┘  │
├─────────────────────────────────────────────────────────────────┤
│                    Processing Layer                              │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
│  │   Process   │  │   Monitor   │  │       Calibration       │  │
│  └─────────────┘  └─────────────┘  └─────────────────────────┘  │
├─────────────────────────────────────────────────────────────────┤
│                    Domain Layer                                  │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
│  │  Algorithm  │  │   Broker    │  │      Market Data        │  │
│  └─────────────┘  └─────────────┘  └─────────────────────────┘  │
├─────────────────────────────────────────────────────────────────┤
│                    Core Layer                                    │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
│  │    Core     │  │  Database   │  │    Message Broker       │  │
│  └─────────────┘  └─────────────┘  └─────────────────────────┘  │
├─────────────────────────────────────────────────────────────────┤
│                    Infrastructure Layer                          │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
│  │ Binance API │  │ Indicators  │  │      WebSocket          │  │
│  └─────────────┘  └─────────────┘  └─────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
```

### Key Components

| Component | Description |
|-----------|-------------|
| **CryptoTrading.App.Core** | Core interfaces, models, database access, positions, trades |
| **CryptoTrading.App.Algorithm** | Trading strategies and custom indicators |
| **CryptoTrading.App.Broker** | Order execution and market interaction |
| **CryptoTrading.App.MarketData** | Live & historical market data feeds |
| **CryptoTrading.App.Monitor** | Trade monitoring and position management |
| **CryptoTrading.App.Process** | Trade processing and backtesting metrics |
| **Binance** | Full Binance API wrapper (REST & WebSocket) |
| **Tulip.NETCore** | 100+ technical indicators library |

## ✨ Features

### Trading Features
- ✅ Live trading with Binance WebSocket streams
- ✅ Market, Limit, and Stop-Limit order types
- ✅ Position management with multiple legs
- ✅ Automatic stop-loss and take-profit orders
- ✅ Multi-timeframe analysis support
- ✅ Real-time candlestick aggregation

### Risk Management
- ✅ ATR-based stop-loss calculation
- ✅ Trailing stop-loss mechanisms
- ✅ Fixed and percentage-based risk limits
- ✅ Position sizing based on account balance
- ✅ Maximum daily volume limits

### Analysis & Backtesting
- ✅ Historical data backtesting
- ✅ Strategy calibration and optimization
- ✅ Performance metrics and reporting
- ✅ Trade history storage in SQL Server

### Technical Infrastructure
- ✅ Dependency injection throughout
- ✅ Internal message broker for event handling
- ✅ Comprehensive logging
- ✅ Configuration management
- ✅ Email notifications for trade alerts

## 📁 Project Structure

```
Src/
├── Binance/                          # Binance API wrapper
│   ├── Account/                      # Account models (balances, orders, etc.)
│   ├── Api/                          # REST API client
│   ├── Cache/                        # Data caching
│   ├── Client/                       # WebSocket clients
│   ├── Market/                       # Market data models
│   ├── Serialization/                # JSON serializers
│   ├── Stream/                       # Stream publishers
│   ├── Utility/                      # Helper utilities
│   └── WebSocket/                    # WebSocket implementation
│
├── CryptoTrading.App.Core/           # Core domain
│   ├── BinanceAccount/               # Account configuration
│   ├── Database/                     # EF contexts and repositories
│   ├── KeyClass/                     # API key management
│   ├── Logging/                      # Custom file logger
│   ├── MarketMonitorFactory/         # Monitor factory
│   ├── Message Broker/               # Internal pub/sub
│   ├── Position/                     # Position management
│   ├── RequestTracker/               # Request tracking
│   ├── Strategy/                     # Strategy interfaces
│   ├── Trade/                        # Trade models
│   └── TradeRequest/                 # Order requests
│
├── CryptoTrading.App.Algorthm/       # Trading algorithms
│   ├── CustomIndicators/             # Custom indicator implementations
│   ├── StopLimits/                   # Stop-loss strategies
│   └── TradingStrategies/            # 30+ trading strategies
│
├── CryptoTrading.App.Broker/         # Order execution
├── CryptoTrading.App.MarketData/     # Market data feeds
├── CryptoTrading.App.Monitor/        # Trade monitoring
├── CryptoTrading.App.Process/        # Trade processing
├── CryptoTrading.App.Calibration/    # Strategy optimization
│
├── Indicators/                       # Tulip.NETCore indicators
│
├── Samples/                          # Example applications
│   ├── BinanceConsoleApp/            # Full-featured console app
│   ├── BinancePriceChart/            # Price charting example
│   ├── BinanceMarketDepth/           # Order book example
│   └── BinanceTradeHistory/          # Trade history example
│
└── Tests/
    ├── IndicatorsTest/               # Indicator unit tests
    └── CryptoTrading.App.Monitor.Tests/ # Monitor tests
```

## 🚀 Getting Started

### Prerequisites

- .NET 9.0 SDK or later
- SQL Server (for trade history storage)
- Binance API key and secret

### Installation

1. **Clone the repository**
   ```bash
   git clone https://github.com/yourusername/CryptoTrading.App.git
   cd CryptoTrading.App/Src
   ```

2. **Restore NuGet packages**
   ```bash
   dotnet restore CryptoTrading.App.sln
   ```

3. **Build the solution**
   ```bash
   dotnet build CryptoTrading.App.sln
   ```

4. **Configure your settings** (see [Configuration](#configuration))

5. **Run the application**
   ```bash
   dotnet run --project CryptoTrading.App.2/CryptoTrading.App.2.csproj -- "YOUR_DATABASE_CONNECTION"
   ```

### Quick Start (Backtesting)

```bash
dotnet run --project CryptoTrading.App.AlgorthmTesting/CryptoTrading.App.AlgorthmTesting.csproj
```

## ⚙️ Configuration

### appsettings.json

```json
{
  "BinanceApi": {
    "ApiKey": "YOUR_API_KEY",
    "ApiSecret": "YOUR_API_SECRET"
  },
  "Database": {
    "ConnectionString": "Data Source=SERVER;Initial Catalog=CryptoDb;Integrated Security=True"
  },
  "Trading": {
    "Interval": "FiveMinutes",
    "Risk": 0.02,
    "NoOfTrades": 3,
    "UseFixedAmount": false,
    "FixedAmount": 100,
    "PercentDailyVolume": 0.01
  },
  "Email": {
    "Server": "smtp.example.com",
    "Port": 587,
    "From": "alerts@example.com",
    "To": "trader@example.com"
  }
}
```

### Supported Candlestick Intervals

| Interval | Description |
|----------|-------------|
| `OneMinute` | 1-minute candles |
| `ThreeMinutes` | 3-minute candles |
| `FiveMinutes` | 5-minute candles |
| `FifteenMinutes` | 15-minute candles |
| `ThirtyMinutes` | 30-minute candles |
| `OneHour` | 1-hour candles |
| `FourHours` | 4-hour candles |
| `OneDay` | Daily candles |

## 📈 Trading Strategies

The application includes 30+ pre-built trading strategies:

### Trend Following
- `MacdTradingStrategy` - MACD crossover with WMA confirmation
- `EmaTradingStrategy` - EMA crossover strategy
- `TrendFollowingTradingStrategy` - Multi-indicator trend following
- `SuperTrendEMATradingStrategy` - SuperTrend with EMA filter

### Mean Reversion
- `MeanReversionTradingStrategy` - Bollinger Band mean reversion
- `BBRsiTradingStrategy` - Bollinger Bands with RSI confirmation

### Momentum
- `RsiTradingStrategy` - RSI overbought/oversold
- `MacdRSITradingStrategy` - MACD and RSI combination
- `AroonStoch` - Aroon and Stochastic combination

### Price Action
- `PriceActionTradingStrategy` - Candlestick pattern recognition
- `HeikinAshiTS` - Heikin-Ashi trend strategy

### Creating Custom Strategies

```csharp
public class MyCustomStrategy : TradingStrategy
{
    public MyCustomStrategy(ILogger<TradingStrategy> logger) : base(logger) { }

    protected override Dictionary<string, IndicatorSetUp> GenerateIndicators()
    {
        return new Dictionary<string, IndicatorSetUp>
        {
            ["EMA20"] = new IndicatorSetUp(Tulip.Indicators.ema, new double[] { 20 }),
            ["RSI"] = new IndicatorSetUp(Tulip.Indicators.rsi, new double[] { 14 })
        };
    }

    protected override double Calculate(
        Dictionary<string, double[][]> indicatorOutputs,
        Candlestick closePrice,
        IStopLimitTracker stopLimitTrackers)
    {
        var ema = indicatorOutputs["EMA20"][0].Last();
        var rsi = indicatorOutputs["RSI"][0].Last();

        // Buy signal: Price above EMA and RSI < 30
        if ((double)closePrice.Close > ema && rsi < 30)
        {
            SetStopLimit(indicatorOutputs, closePrice, stopLimitTrackers);
            return 1; // Buy signal
        }

        return 0; // Hold
    }
}
```

## 📊 Technical Indicators

The application uses the Tulip Indicators library with 100+ indicators:

### Trend Indicators
- EMA, SMA, WMA, DEMA, TEMA
- MACD, ADX, Parabolic SAR
- Aroon, SuperTrend

### Momentum Indicators
- RSI, Stochastic, Stochastic RSI
- CCI, Williams %R, MFI

### Volatility Indicators
- Bollinger Bands, ATR, Keltner Channels
- Standard Deviation, Donchian Channels

### Volume Indicators
- OBV, VWAP, VWMA
- Volume Rate of Change

## 🔌 API Reference

### IBroker Interface

```csharp
public interface IBroker
{
    void ClosePosition(ITrade trade);
}
```

### ITradingStrategy Interface

```csharp
public interface ITradingStrategy
{
    int OutputLength { get; }
    double Calculate(CandleStickDictionary candleSticks, IStopLimitTracker stopLimitTrackers);
    void Log(string v);
}
```

### IMarketMonitor Interface

```csharp
public interface IMarketMonitor
{
    Task Subscribe(string symbol, string keyValue, Action<CandlestickEventArgs> callback);
    Task<IEnumerable<Candlestick>> GetHistoricCandleSticks(string symbol);
    Task CheckOrder(ITransaction transaction);
    bool IsSubscribed(string symbol, string keyValue);
}
```

## 🧪 Testing

### Running Unit Tests

```bash
dotnet test CryptoTrading.App.sln
```

### Running Specific Test Projects

```bash
# Indicator tests
dotnet test IndicatorsTest/IndicatorsTest.csproj

# Monitor tests
dotnet test CryptoTrading.App.Monitor.Tests/CryptoTrading.App.Monitor.Tests.csproj
```

## 🤝 Contributing

1. Fork the repository
2. Create a feature branch (`git checkout -b feature/AmazingFeature`)
3. Commit your changes (`git commit -m 'Add some AmazingFeature'`)
4. Push to the branch (`git push origin feature/AmazingFeature`)
5. Open a Pull Request

### Coding Standards

- Follow C# naming conventions
- Use dependency injection for all services
- Write unit tests for new functionality
- Document public APIs with XML comments

## ⚠️ Disclaimer

**IMPORTANT: This software is for educational purposes only.**

- Cryptocurrency trading involves substantial risk of loss
- Past performance does not guarantee future results
- Never trade with money you cannot afford to lose
- Always test strategies thoroughly with backtesting before live trading
- The authors are not responsible for any financial losses

---

## 📄 License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## 🙏 Acknowledgments

- [Binance API](https://binance-docs.github.io/apidocs/) - Exchange API
- [Tulip Indicators](https://tulipindicators.org/) - Technical analysis library
- [Skender.Stock.Indicators](https://github.com/DaveSkender/Stock.Indicators) - Additional indicators

---

**Built with ❤️ for algorithmic traders**
