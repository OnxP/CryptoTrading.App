using System;
using System.Collections.Generic;
using System.Linq;

namespace CryptoTrading.App.Core.Database.Config
{
    /// <summary>
    /// Seeds default strategy parameters into the database with optimization bounds.
    /// Run once to populate the StrategyParameters table with current hardcoded values.
    /// </summary>
    public static class StrategyParameterSeeder
    {
        public static void SeedDefaults(string dataSource)
        {
            var parameters = GetDefaultParameters();

            using (var context = new StrategyParameterContext(dataSource))
            {
                foreach (var param in parameters)
                {
                    var existing = context.StrategyParameters
                        .FirstOrDefault(p => p.StrategyName == param.StrategyName
                                          && p.ParameterName == param.ParameterName);

                    if (existing == null)
                    {
                        context.StrategyParameters.Add(param);
                    }
                }

                context.SaveChanges();
            }
        }

        public static List<StrategyParameterDb> GetDefaultParameters()
        {
            var parameters = new List<StrategyParameterDb>();

            // === Regime Detection Parameters ===
            AddParam(parameters, "RegimeBasedMarketStructure", "EmaGradientPeriod", 5, 3, 10, 1, "Regime");
            AddParam(parameters, "RegimeBasedMarketStructure", "GradientLookback", 3, 2, 8, 1, "Regime");
            AddParam(parameters, "RegimeBasedMarketStructure", "TrendThreshold", 0.05, 0.01, 0.15, 0.01, "Regime");
            AddParam(parameters, "RegimeBasedMarketStructure", "VolatilityHighPercentile", 75, 60, 90, 5, "Regime");
            AddParam(parameters, "RegimeBasedMarketStructure", "VolatilityLowPercentile", 25, 10, 40, 5, "Regime");
            AddParam(parameters, "RegimeBasedMarketStructure", "AtrPeriod", 14, 7, 21, 1, "Regime");

            // === Setup Parameters ===
            AddParam(parameters, "RegimeBasedSetup", "MinRiskRewardRatio", 1.5, 1.0, 3.0, 0.25, "Setup");
            AddParam(parameters, "RegimeBasedSetup", "MacdFast", 12, 8, 16, 2, "Setup");
            AddParam(parameters, "RegimeBasedSetup", "MacdSlow", 26, 20, 34, 2, "Setup");
            AddParam(parameters, "RegimeBasedSetup", "MacdSignal", 9, 5, 13, 2, "Setup");
            AddParam(parameters, "RegimeBasedSetup", "BbPeriod", 20, 14, 30, 2, "Setup");
            AddParam(parameters, "RegimeBasedSetup", "BbStdDev", 2.0, 1.5, 3.0, 0.25, "Setup");
            AddParam(parameters, "RegimeBasedSetup", "RsiPeriod", 14, 7, 21, 1, "Setup");
            AddParam(parameters, "RegimeBasedSetup", "RsiOversold", 35, 20, 45, 5, "Setup");
            AddParam(parameters, "RegimeBasedSetup", "RsiOverbought", 65, 55, 80, 5, "Setup");
            AddParam(parameters, "RegimeBasedSetup", "ZoneProximityMultiple", 2.0, 1.0, 4.0, 0.5, "Setup");

            // === Zone Detection Parameters ===
            AddParam(parameters, "SupplyDemandZone", "LookbackPeriods", 50, 20, 100, 10, "Zone");
            AddParam(parameters, "SupplyDemandZone", "BodyToRangeThreshold", 0.4, 0.2, 0.6, 0.05, "Zone");
            AddParam(parameters, "SupplyDemandZone", "DepartureMultiple", 1.5, 1.0, 3.0, 0.25, "Zone");
            AddParam(parameters, "SupplyDemandZone", "MaxZones", 6, 3, 10, 1, "Zone");
            AddParam(parameters, "SupplyDemandZone", "MaxTouches", 3, 2, 5, 1, "Zone");

            // === Entry Parameters ===
            AddParam(parameters, "StochRsiEntry", "RsiPeriod", 14, 7, 21, 1, "Entry");
            AddParam(parameters, "StochRsiEntry", "StochPeriod", 14, 7, 21, 1, "Entry");
            AddParam(parameters, "StochRsiEntry", "SignalPeriod", 3, 2, 5, 1, "Entry");
            AddParam(parameters, "StochRsiEntry", "SmoothPeriod", 3, 2, 5, 1, "Entry");
            AddParam(parameters, "StochRsiEntry", "OversoldThreshold", 20, 10, 30, 5, "Entry");
            AddParam(parameters, "StochRsiEntry", "OverboughtThreshold", 80, 70, 90, 5, "Entry");

            // === Exit Parameters ===
            AddParam(parameters, "TrailingStopExit", "TrailingStartMultiple", 1.0, 0.5, 2.0, 0.25, "Exit");
            AddParam(parameters, "TrailingStopExit", "TrailingAtrMultiple", 1.5, 1.0, 3.0, 0.25, "Exit");
            AddParam(parameters, "ScaleOutExit", "FirstExitRMultiple", 1.0, 0.5, 2.0, 0.25, "Exit");
            AddParam(parameters, "ScaleOutExit", "SecondExitRMultiple", 2.0, 1.5, 3.0, 0.25, "Exit");
            AddParam(parameters, "ScaleOutExit", "ThirdExitRMultiple", 3.0, 2.0, 5.0, 0.5, "Exit");
            AddParam(parameters, "ScaleOutExit", "PartialExitQuantity", 0.33, 0.2, 0.5, 0.05, "Exit");
            AddParam(parameters, "TimeBasedExit", "TimeStopBars", 30, 10, 60, 5, "Exit");
            AddParam(parameters, "TimeBasedExit", "StagnationThreshold", 0.5, 0.2, 1.0, 0.1, "Exit");

            return parameters;
        }

        private static void AddParam(List<StrategyParameterDb> list,
            string strategyName, string paramName, double value,
            double min, double max, double step, string category)
        {
            list.Add(new StrategyParameterDb
            {
                StrategyName = strategyName,
                ParameterName = paramName,
                Value = value,
                MinValue = min,
                MaxValue = max,
                StepSize = step,
                Category = category,
                ParameterSetId = 1,
                IsActive = true,
                LastModified = DateTime.UtcNow
            });
        }
    }
}
