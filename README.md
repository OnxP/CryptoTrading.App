# CryptoTrading.App

A comprehensive algorithmic cryptocurrency trading application built with .NET 9.0, supporting multi-exchange trading, advanced regime-based strategies, and ML-powered market analysis.

![.NET](https://img.shields.io/badge/.NET-9.0-512BD4)
![License](https://img.shields.io/badge/license-MIT-blue)
![Platform](https://img.shields.io/badge/exchange-Binance%20%7C%20Bitfinex-F0B90B)

## Table of Contents

- [Overview](#overview)
- [Architecture](#architecture)
- [Project Structure](#project-structure)
- [Prerequisites](#prerequisites)
- [Getting Started](#getting-started)
- [Usage Scenarios](#usage-scenarios)
  - [1. Data Load](#1-data-load)
  - [2. Backtesting](#2-backtesting-algorithm-from-database)
  - [3. Live Testing](#3-live-testing-paper-trading)
  - [4. Live Trading](#4-live-trading)
  - [5. Dashboard UI](#5-dashboard-ui)
- [Configuration](#configuration)
- [Trading Strategies](#trading-strategies)
- [Multi-Exchange Support](#multi-exchange-support)
- [Deployment](#deployment)
- [Testing](#testing)
- [Roadmap](#roadmap)
- [Disclaimer](#disclaimer)

## Overview

CryptoTrading.App is a modular, extensible algorithmic trading platform designed for automated cryptocurrency trading. It supports:

- **Multi-Exchange Trading**: Binance and Bitfinex with exchange-agnostic abstractions
- **Live Trading**: Real-time order execution via WebSocket streams
- **Backtesting**: Historical data analysis with comprehensive metrics (Sharpe, Sortino, Calmar, drawdown)
- **Regime-Based Strategy**: Multi-timeframe approach (4H regime, 15M setup, 1M execution)
- **30+ Pre-built Strategies**: Trend following, mean reversion, momentum, and price action
- **Risk Management**: ATR-based stops, trailing stops, scale-out exits, leverage probability scoring
- **Strategy Optimization**: Grid search and walk-forward parameter optimization
- **ML Integration**: LightGBM regime classification with hot-swap model reloading
- **React Dashboard**: TradingView charts with real-time SignalR updates

## Architecture

```
+-------------------------------------------------------------------+
|                      Application Layer                             |
|  +-----------+  +-------------+  +-----------+  +---------------+ |
|  | Main App  |  | Backtesting |  |   API     |  |   Dashboard   | |
|  | (Console) |  | (Console)   |  | (ASP.NET) |  |   (React)     | |
|  +-----------+  +-------------+  +-----------+  +---------------+ |
+-------------------------------------------------------------------+
|                      Processing Layer                              |
|  +-----------+  +-----------+  +-----------+  +-----------------+ |
|  |  Process  |  |  Monitor  |  |Calibration|  |  Optimization   | |
|  +-----------+  +-----------+  +-----------+  +-----------------+ |
+-------------------------------------------------------------------+
|                      Domain Layer                                  |
|  +-----------+  +-----------+  +-----------+  +-----------------+ |
|  | Algorithm |  |  Broker   |  |MarketData |  |       ML        | |
|  +-----------+  +-----------+  +-----------+  +-----------------+ |
+-------------------------------------------------------------------+
|                      Core Layer                                    |
|  +-----------+  +-----------+  +-----------+  +-----------------+ |
|  |   Core    |  | Database  |  |  Exchange |  | Message Broker  | |
|  +-----------+  +-----------+  +-----------+  +-----------------+ |
+-------------------------------------------------------------------+
|                      Exchange Adapters                             |
|  +-----------+  +-----------+  +-----------+  +-----------------+ |
|  |  Binance  |  | Bitfinex  |  |Indicators |  |   WebSocket     | |
|  +-----------+  +-----------+  +-----------+  +-----------------+ |
+-------------------------------------------------------------------+
```

## Project Structure

```
Src/
+-- CryptoTrading.App/                   # Main console application (entry point)
+-- CryptoTrading.App.Core/              # Core domain: interfaces, models, database, exchange abstractions
|   +-- Database/Config/                 # EF6 contexts, strategy parameters, exchange configs
|   +-- Exchange/                        # Exchange-agnostic types (IExchangeProvider, models)
|   +-- Position/                        # Position and trade management
|   +-- Strategy/                        # Strategy interfaces
|   +-- Trade/                           # Trade models, BacktestMetrics
+-- CryptoTrading.App.Algorthm/          # Trading algorithms
|   +-- RegimeBased/                     # Multi-timeframe regime strategy (23 files)
|   +-- TradingStrategies/               # 30+ pre-built strategies
|   +-- CustomIndicators/                # Custom indicator implementations
+-- CryptoTrading.App.Broker/            # Order execution layer
+-- CryptoTrading.App.MarketData/        # Live & historical data feeds
+-- CryptoTrading.App.Monitor/           # Trade monitoring and position management
+-- CryptoTrading.App.Process/           # Trade processing and orchestration
+-- CryptoTrading.App.DatabaseLoad/      # Candlestick data ingestion from Binance
+-- CryptoTrading.App.AlgorthmTesting/   # Backtesting framework
+-- CryptoTrading.App.Exchange.Binance/  # Binance IExchangeProvider adapter
+-- CryptoTrading.App.Exchange.Bitfinex/ # Bitfinex IExchangeProvider adapter
+-- CryptoTrading.App.Optimization/      # Grid search & walk-forward optimizer
+-- CryptoTrading.App.ML/               # ML.NET regime classification
+-- CryptoTrading.App.Api/              # REST API (ASP.NET Core + Swagger)
+-- CryptoTrading.App.Dashboard/         # React + TradingView Lightweight Charts UI
+-- CryptoTrading.App.Tests/             # Unit tests (xUnit + FluentAssertions)
+-- Binance/                             # Full Binance API wrapper (REST & WebSocket)
+-- Indicators/                          # Tulip.NETCore - 100+ technical indicators
```

## Prerequisites

- **.NET 9.0 SDK** or later
- **SQL Server** instance (e.g. `ANKUR-PC\APDATASERVICE`)
- **Binance API key and secret** (for data loading and live trading)
- **Node.js 18+** (for Dashboard UI only)

### Database Setup

The application uses SQL Server with `CryptoDb` as the database name. The connection uses Windows Integrated Security:

```
Data Source=YOUR_SERVER\INSTANCE;Initial Catalog=CryptoDb;Integrated Security=True
```

Key tables are created automatically by Entity Framework:
- `CandleStickDbs` - OHLCV candlestick data
- `CryptoConfigs` - Runtime configuration (RunType, API keys, trading parameters)
- `Trades` - Historical trade results
- `ExchangeConfigs` - Multi-exchange connection settings
- `StrategyParameters` - Externalized strategy parameters with optimization bounds
- `AccountSnapshots` - Daily balance snapshots per exchange

## Getting Started

### 1. Clone and Build

```bash
git clone https://github.com/OnxP/CryptoTrading.App.git
cd CryptoTrading.App/Src
dotnet restore CryptoTrading.App.sln
dotnet build CryptoTrading.App.sln --configuration Release
```

### 2. Configure Database

Update the database server name in `CryptoConfigs` table (Id=1) or pass it as a command-line argument.

### 3. Configure API Keys

Set your Binance API credentials in `appsettings.json` (for DatabaseLoad) or in the `CryptoConfigs` database table (for live trading).

---

## Usage Scenarios

### 1. Data Load

**Purpose**: Download historical candlestick data from Binance and store it in SQL Server for backtesting.

**Project**: `CryptoTrading.App.DatabaseLoad`

**What it does**:
- Connects to Binance REST API to fetch OHLCV candlestick data
- Detects gaps in existing data using `MissingCandleDetector`
- Fills only missing data (incremental, not full re-download)
- Downloads multiple intervals: 1m, 5m, 15m, 1h, 4h
- Processes up to 10 concurrent API calls for performance
- Stores everything in the `CandleStickDbs` table

**Configuration** (`appsettings.json`):
```json
{
  "User": {
    "ApiKey": "YOUR_BINANCE_API_KEY",
    "ApiSecret": "YOUR_BINANCE_API_SECRET"
  },
  "ApiOptions": {
    "EndpointUrl": "https://api.binance.com",
    "RecvWindowDefault": 15000,
    "RequestRateLimit": { "Count": 1200, "DurationMinutes": 1 }
  }
}
```

**Running**:
```bash
cd Src
dotnet run --project CryptoTrading.App.DatabaseLoad/CryptoTrading.App.DatabaseLoad.csproj
```

**Notes**:
- First run may take several hours depending on how many symbols and intervals you load
- Subsequent runs only fill gaps, so they are much faster
- The date range and symbols are configured in `Program.cs`
- Respects Binance rate limits (1200 requests/minute)

---

### 2. Backtesting (Algorithm from Database)

**Purpose**: Test trading algorithms against historical data stored in the database, without risking real money.

**Project**: `CryptoTrading.App.AlgorthmTesting`

**What it does**:
- Loads historical candlestick data from SQL Server
- Runs the selected trading algorithm against each candle sequentially
- Tracks simulated positions, entries, exits, and P&L
- Outputs trade results to both a text file and the database
- Calculates performance metrics (win rate, total return, trade count)

**Configuration** (in `RunContext.cs`):
- `Indicator stratgy` - Which algorithm to test
- `NoOfTrades` - Maximum concurrent trades
- `Risk` - Risk percentage per trade
- `From` / `To` - Backtest date range
- Output path: `C:\Temp\{strategy_name}\TradeResults_{params}.txt`

**Running**:
```bash
cd Src
dotnet run --project CryptoTrading.App.AlgorthmTesting/CryptoTrading.App.AlgorthmTesting.csproj
```

**Example Output**:
```
Strategy: RegimeBased | Symbol: BTCUSDT | Timeframe: 15m
Period: 2024-01-01 to 2024-06-01
Total Trades: 142 | Winners: 85 (59.9%) | Losers: 57 (40.1%)
Total Return: 34.7% | Max Drawdown: 8.2%
```

**Notes**:
- Ensure you have loaded data for the backtest period using the Data Load step first
- Modify `Program.cs` to select different strategies, symbols, or date ranges
- Results are stored in the `Trades` table for comparison

---

### 3. Live Testing (Paper Trading)

**Purpose**: Run the algorithm with real-time market data but simulated orders. No real money is at risk.

**Project**: `CryptoTrading.App` (main console application)

**What it does**:
- Connects to Binance WebSocket streams for real-time candlestick data
- Runs the trading algorithm on each new candle as it arrives
- Simulates order execution (TestBroker) - no real orders are placed
- Reads live account balances but does not modify them
- Runs continuously with a 2-minute refresh cycle
- Monitors for configuration changes every 2 minutes

**Setup**:
1. Set `RunType = LiveTesting` in the `CryptoConfigs` database table
2. Ensure API keys are configured in the database

**Running**:
```bash
cd Src
# Uses default database server (ANKUR-PC\APDATASERVICE)
dotnet run --project CryptoTrading.App/CryptoTrading.App.csproj

# Or specify a custom database server
dotnet run --project CryptoTrading.App/CryptoTrading.App.csproj -- "YOUR_SERVER\INSTANCE"
```

**Process Loop**:
- Every 2 minutes: Checks `CryptoConfig.EndProcess` flag - set to `true` to gracefully stop
- Every 60 minutes: Refreshes position balances
- Daily at 21:17 UTC: Archives trades and refreshes symbols

**Stopping**:
Set `EndProcess = true` in the `CryptoConfigs` database table. The application will stop at the next 2-minute check.

---

### 4. Live Trading

**Purpose**: Run the algorithm with real-time data and execute real orders on the exchange.

**Project**: `CryptoTrading.App` (same as Live Testing)

> **WARNING**: This mode places REAL orders with REAL money. Ensure thorough backtesting and live testing before enabling this mode. Start with small position sizes.

**What it does**:
- Everything Live Testing does, PLUS:
- Places real market, limit, and stop-limit orders via Binance API
- Manages real positions with automatic stop-loss and take-profit
- Sends email notifications on trade events
- Records all trades and position changes to the database

**Setup**:
1. Set `RunType = Live` in the `CryptoConfigs` database table
2. Ensure API keys have trading permissions enabled on Binance
3. Configure email notification settings in `CryptoConfigs`
4. Set appropriate risk parameters (`Risk`, `NoOfTrades`, `UseFixedAmount`, `FixedAmount`)

**Running**:
```bash
cd Src
dotnet run --project CryptoTrading.App/CryptoTrading.App.csproj -- "YOUR_SERVER\INSTANCE"
```

**Key Configuration** (in `CryptoConfigs` table):
| Parameter | Description | Example |
|-----------|-------------|---------|
| `RunType` | `Live` for real trading | `Live` |
| `NoOfTrades` | Max concurrent positions | `3` |
| `Risk` | Risk % per trade | `0.02` |
| `UseFixedAmount` | Use fixed size or % of balance | `true` |
| `FixedAmount` | Fixed trade amount in quote asset | `100` |
| `PercentDailyVolume` | Max % of daily volume per trade | `0.01` |
| `Interval` | Candlestick timeframe | `FifteenMinutes` |

**Safety Features**:
- Set `EndProcess = true` in DB to gracefully stop at next refresh
- Position size limits prevent oversized orders
- Daily volume limits prevent market impact
- Email alerts for trade events and errors

---

### 5. Dashboard UI

**Purpose**: Visual interface for monitoring trading activity, viewing charts, and managing strategy parameters.

**Project**: `CryptoTrading.App.Dashboard` (React) + `CryptoTrading.App.Api` (ASP.NET Core backend)

**What it provides**:
- TradingView Lightweight Charts with candlestick display
- Regime zone overlays (Bull/Bear/Ranging colored backgrounds)
- Supply/demand zone visualization
- Trade entry/exit markers on chart
- Performance metrics panel (Sharpe ratio, drawdown, win rate, profit factor)
- Live state panel (current regime, volatility, open trades)
- Strategy parameter editor with live API integration
- Real-time updates via SignalR WebSocket

**Starting the API backend**:
```bash
cd Src
dotnet run --project CryptoTrading.App.Api/CryptoTrading.App.Api.csproj
# API runs at http://localhost:5000
# Swagger UI at http://localhost:5000/swagger
```

**Starting the Dashboard**:
```bash
cd Src/CryptoTrading.App.Dashboard
npm install
npm run dev
# Dashboard runs at http://localhost:5173
```

**API Endpoints**:
| Method | Endpoint | Description |
|--------|----------|-------------|
| GET | `/api/strategies` | List strategy names |
| GET/PUT | `/api/strategies/{name}/parameters` | Get/update parameters |
| POST | `/api/strategies/{name}/parameters/reset` | Reset to defaults |
| GET | `/api/chart/{symbol}/candles` | OHLCV candlestick data |
| GET | `/api/chart/{symbol}/regimes` | Regime zone periods |
| GET | `/api/chart/{symbol}/signals` | Trade signals |
| GET | `/api/chart/{symbol}/zones` | Supply/demand zones |
| GET | `/api/exchanges` | List exchanges |
| GET | `/api/exchanges/{id}/balances` | Exchange balances |
| POST | `/api/exchanges/{id}/refresh` | Trigger account refresh |
| GET | `/api/live/state` | Current algorithm state |
| GET | `/api/performance/summary` | Strategy metrics |

---

## Configuration

### Runtime Configuration (Database)

All runtime settings are stored in the `CryptoConfigs` table (Id=1). Key settings:

```
RunType             : BackTesting | LiveTesting | Live
EndProcess          : false (set true to stop)
Interval            : FifteenMinutes
NoOfTrades          : 3
Risk                : 0.02
ApiKey              : [Binance API Key]
ApiKeySecret        : [Binance API Secret]
From / To           : Date range for backtesting
EmailServer/Port    : SMTP settings for alerts
```

### Strategy Parameters (Database)

All strategy parameters are externalized to the `StrategyParameters` table with optimization bounds:

```sql
-- Example: View all parameters for the regime strategy
SELECT StrategyName, ParameterName, Value, MinValue, MaxValue, StepSize
FROM StrategyParameters
WHERE IsActive = 1
ORDER BY Category, StrategyName
```

Parameters auto-reload every 60 seconds without requiring a restart.

---

## Trading Strategies

### Regime-Based Multi-Timeframe Strategy (Primary)

The flagship strategy operates across three timeframes:

1. **4-Hour (Regime Detection)**: Classifies the market as Bull, Bear, or Ranging using EMA gradients, ATR percentiles, and volatility analysis
2. **15-Minute (Setup)**: Evaluates MACD divergence, Bollinger Band reversals, and supply/demand zones for trade setups
3. **1-Minute (Execution)**: Precise entry timing using Stochastic RSI crossovers

**Leverage Probability Scoring**: Each trade setup receives a confidence score (0.0 - 1.0) based on:
- Regime confidence (EMA gradient strength, volatility)
- Setup confidence (momentum, zone confluence, risk/reward ratio)
- The confidence score determines recommended leverage (currently capped at 1x by default)

### Pre-built Strategies (30+)

| Category | Strategies |
|----------|------------|
| **Trend Following** | MACD, EMA Crossover, SuperTrend+EMA, Trend Following |
| **Mean Reversion** | Bollinger Band, BB+RSI |
| **Momentum** | RSI, MACD+RSI, Aroon+Stochastic |
| **Price Action** | Candlestick Patterns, Heikin-Ashi |

---

## Multi-Exchange Support

The application uses an exchange-agnostic abstraction layer (`IExchangeProvider`) that supports multiple exchanges:

| Exchange | Status | Fee (Maker/Taker) |
|----------|--------|-------------------|
| Binance | Implemented | 0.1% / 0.1% (BNB discount: 25%) |
| Bitfinex | Implemented | 0.1% / 0.2% |

Each exchange has:
- Isolated accounting (positions, balances, trades)
- Separate data feeds
- Independent fee schedules
- Daily account snapshot refresh

---

## Deployment

### Current Deployment Procedure

The application is deployed by building locally, pushing to a release branch, and pulling on the server:

#### 1. Build the Release

```bash
# On your development machine
cd CryptoTrading.App/Src
dotnet build CryptoTrading.App.sln --configuration Release
dotnet publish CryptoTrading.App/CryptoTrading.App.csproj -c Release -o ../publish/app
dotnet publish CryptoTrading.App.Api/CryptoTrading.App.Api.csproj -c Release -o ../publish/api
```

#### 2. Commit and Push to Release Branch

```bash
cd ..
git checkout release
git merge EntryStratigies
git add publish/
git commit -m "Release build $(date +%Y-%m-%d)"
git push origin release
```

#### 3. Deploy on the Server

```bash
# SSH into the server
ssh your-server

# Pull the latest release
cd /opt/cryptotrading
git pull origin release

# Stop the running service
sudo systemctl stop cryptotrading

# Update the binaries
cp -r publish/app/* /opt/cryptotrading/app/
cp -r publish/api/* /opt/cryptotrading/api/

# Start the service
sudo systemctl start cryptotrading
```

#### 4. Verify the Deployment

```bash
# Check the service is running
sudo systemctl status cryptotrading

# Tail the logs
tail -f /opt/cryptotrading/logs/cryptotrading.log

# Check the API is responding
curl http://localhost:5000/api/live/state
```

### Setting Up as a Systemd Service (Linux)

Create `/etc/systemd/system/cryptotrading.service`:

```ini
[Unit]
Description=CryptoTrading Algorithm
After=network.target

[Service]
Type=simple
User=trading
WorkingDirectory=/opt/cryptotrading/app
ExecStart=/usr/bin/dotnet CryptoTrading.App.dll "YOUR_SERVER\\INSTANCE"
Restart=on-failure
RestartSec=10
StandardOutput=journal
StandardError=journal

[Install]
WantedBy=multi-user.target
```

Create `/etc/systemd/system/cryptotrading-api.service`:

```ini
[Unit]
Description=CryptoTrading API
After=network.target

[Service]
Type=simple
User=trading
WorkingDirectory=/opt/cryptotrading/api
ExecStart=/usr/bin/dotnet CryptoTrading.App.Api.dll
Environment=ASPNETCORE_URLS=http://0.0.0.0:5000
Restart=on-failure
RestartSec=10

[Install]
WantedBy=multi-user.target
```

Enable and start:
```bash
sudo systemctl daemon-reload
sudo systemctl enable cryptotrading cryptotrading-api
sudo systemctl start cryptotrading cryptotrading-api
```

### Setting Up as a Windows Service

Use NSSM (Non-Sucking Service Manager) to run as a Windows service:

```batch
REM Install the trading service
nssm install CryptoTrading "C:\dotnet\dotnet.exe" "CryptoTrading.App.dll YOUR_SERVER\INSTANCE"
nssm set CryptoTrading AppDirectory "C:\CryptoTrading\app"
nssm set CryptoTrading Start SERVICE_AUTO_START

REM Install the API service
nssm install CryptoTradingApi "C:\dotnet\dotnet.exe" "CryptoTrading.App.Api.dll"
nssm set CryptoTradingApi AppDirectory "C:\CryptoTrading\api"
nssm set CryptoTradingApi AppEnvironmentExtra "ASPNETCORE_URLS=http://0.0.0.0:5000"
nssm set CryptoTradingApi Start SERVICE_AUTO_START

REM Start both
nssm start CryptoTrading
nssm start CryptoTradingApi
```

### Dashboard Deployment

For the React dashboard, build and serve as static files:

```bash
cd Src/CryptoTrading.App.Dashboard
npm install
npm run build
# Output in dist/ - serve with nginx, IIS, or any static file server
```

Nginx configuration:
```nginx
server {
    listen 80;
    server_name trading.yourdomain.com;

    location / {
        root /opt/cryptotrading/dashboard/dist;
        try_files $uri $uri/ /index.html;
    }

    location /api/ {
        proxy_pass http://localhost:5000;
    }

    location /hubs/ {
        proxy_pass http://localhost:5000;
        proxy_http_version 1.1;
        proxy_set_header Upgrade $http_upgrade;
        proxy_set_header Connection "upgrade";
    }
}
```

---

## Testing

### Running All Tests

```bash
cd Src
dotnet test CryptoTrading.App.Tests/CryptoTrading.App.Tests.csproj --verbosity normal
```

### Test Coverage (117+ tests)

| Test Suite | Tests | Description |
|------------|-------|-------------|
| Exchange Domain Models | 56 | ExchangeOrder, Balance, Symbol, Candlestick, FeeSchedule |
| Exchange Isolation | 15 | Position isolation, candlestick routing, trade isolation |
| Binance Mapper | 30 | All Binance type conversions and round-trips |
| Bitfinex Mapper | 15 | Symbol format, order mapping, candlestick parsing |
| BacktestMetrics | 11 | Sharpe, Sortino, drawdown, profit factor, expectancy |

---

## Roadmap

| Phase | Branch | Status | Description |
|-------|--------|--------|-------------|
| 6 | `phase6-multi-exchange-adapters` | In Progress | Multi-exchange adapters, broker refactoring |
| 1 | `phase1-parameter-externalization` | Scaffolded | Strategy parameter externalization |
| 2 | `phase2-optimization-engine` | Scaffolded | Grid search & walk-forward optimization |
| 3 | `phase3-api-hot-reload` | Scaffolded | REST API & parameter hot-reload |
| 4 | `phase4-ai-ml-integration` | Scaffolded | ML.NET regime classification |
| 5 | `phase5-visualization-ui` | Scaffolded | React + TradingView dashboard |

---

## Disclaimer

**WARNING: This software is for educational purposes only.**

- Cryptocurrency trading involves substantial risk of loss
- Past performance does not guarantee future results
- Never trade with money you cannot afford to lose
- Always test strategies thoroughly with backtesting and live testing before real trading
- The authors are not responsible for any financial losses

---

## License

This project is licensed under the MIT License - see the [LICENSE](LICENSE) file for details.

## Acknowledgments

- [Binance API](https://binance-docs.github.io/apidocs/) - Exchange API
- [Tulip Indicators](https://tulipindicators.org/) - Technical analysis library
- [Skender.Stock.Indicators](https://github.com/DaveSkender/Stock.Indicators) - Additional indicators
- [TradingView Lightweight Charts](https://github.com/nicorichard/lightweight-charts) - Chart rendering
- [ML.NET](https://dotnet.microsoft.com/en-us/apps/machinelearning-ai/ml-dotnet) - Machine learning framework
