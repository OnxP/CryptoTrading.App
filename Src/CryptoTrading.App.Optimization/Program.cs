using System;

namespace CryptoTrading.App.Optimization
{
    /// <summary>
    /// CLI entry point for the strategy optimization engine.
    /// Usage: CryptoTrading.App.Optimization [strategy] [startDate] [endDate] [optimizer] [objective]
    /// </summary>
    class Program
    {
        static void Main(string[] args)
        {
            Console.WriteLine("=== CryptoTrading Strategy Optimizer ===");
            Console.WriteLine();

            // TODO: Parse CLI arguments
            // - Strategy name (default: RegimeBasedMarketStructure)
            // - Date range (default: last 12 months)
            // - Optimizer type: "grid" or "walkforward" (default: grid)
            // - Objective: "sharpe", "calmar", "composite" (default: sharpe)

            // TODO: Wire up DI container with:
            // - InMemoryStrategyParameters
            // - DbMarketData for BTCUSDT historical candles
            // - ParameterSpace loaded from StrategyParameters table bounds
            // - GridSearchOptimizer or WalkForwardOptimizer

            Console.WriteLine("Optimization engine scaffolding complete.");
            Console.WriteLine("Full implementation pending Phase 1 (parameter externalization) integration.");
        }
    }
}
