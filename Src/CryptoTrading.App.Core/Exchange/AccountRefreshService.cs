using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace CryptoTrading.App.Core.Exchange
{
    /// <summary>
    /// Service that refreshes account data from exchange providers.
    /// Supports daily or configurable-interval refresh per exchange.
    /// </summary>
    public class AccountRefreshService
    {
        private readonly Dictionary<string, IExchangeProvider> _providers;
        private readonly Dictionary<string, DateTime> _lastRefresh;
        private readonly Dictionary<string, ExchangeFeeSchedule> _feeSchedules;
        private readonly Dictionary<string, List<ExchangeBalance>> _lastSnapshots;

        public AccountRefreshService()
        {
            _providers = new Dictionary<string, IExchangeProvider>();
            _lastRefresh = new Dictionary<string, DateTime>();
            _feeSchedules = new Dictionary<string, ExchangeFeeSchedule>();
            _lastSnapshots = new Dictionary<string, List<ExchangeBalance>>();
        }

        /// <summary>
        /// Register an exchange provider for periodic refresh
        /// </summary>
        public void RegisterProvider(IExchangeProvider provider)
        {
            _providers[provider.ExchangeId] = provider;
        }

        /// <summary>
        /// Refresh account data for a specific exchange
        /// </summary>
        public async Task<AccountRefreshResult> RefreshAsync(string exchangeId)
        {
            if (!_providers.TryGetValue(exchangeId, out var provider))
                throw new InvalidOperationException($"Exchange provider '{exchangeId}' not registered");

            var result = new AccountRefreshResult { ExchangeId = exchangeId };

            try
            {
                // Refresh balances
                var balances = (await provider.GetBalancesAsync()).ToList();
                result.Balances = balances;

                // Refresh symbols
                var symbols = (await provider.GetSymbolsAsync()).ToList();
                result.Symbols = symbols;

                // Refresh fee schedule
                var fees = await provider.GetFeeScheduleAsync();
                _feeSchedules[exchangeId] = fees;
                result.FeeSchedule = fees;

                // Detect changes from last snapshot
                if (_lastSnapshots.TryGetValue(exchangeId, out var previousBalances))
                {
                    result.BalanceChanges = DetectBalanceChanges(previousBalances, balances);
                }

                _lastSnapshots[exchangeId] = balances;
                _lastRefresh[exchangeId] = DateTime.UtcNow;
                result.Success = true;
                result.RefreshedAt = DateTime.UtcNow;
            }
            catch (Exception ex)
            {
                result.Success = false;
                result.Error = ex.Message;
            }

            return result;
        }

        /// <summary>
        /// Check if a specific exchange needs refresh based on its configured interval
        /// </summary>
        public bool NeedsRefresh(string exchangeId, int intervalMinutes = 1440)
        {
            if (!_lastRefresh.TryGetValue(exchangeId, out var lastRefresh))
                return true;

            return DateTime.UtcNow - lastRefresh > TimeSpan.FromMinutes(intervalMinutes);
        }

        /// <summary>
        /// Get the cached fee schedule for an exchange
        /// </summary>
        public ExchangeFeeSchedule GetFeeSchedule(string exchangeId)
        {
            return _feeSchedules.TryGetValue(exchangeId, out var schedule) ? schedule : null;
        }

        /// <summary>
        /// Get the last known balances for an exchange
        /// </summary>
        public IReadOnlyList<ExchangeBalance> GetLastBalances(string exchangeId)
        {
            return _lastSnapshots.TryGetValue(exchangeId, out var balances)
                ? balances.AsReadOnly()
                : new List<ExchangeBalance>().AsReadOnly();
        }

        private List<BalanceChange> DetectBalanceChanges(
            List<ExchangeBalance> previous, List<ExchangeBalance> current)
        {
            var changes = new List<BalanceChange>();
            var previousDict = previous.ToDictionary(b => b.Asset, b => b);

            foreach (var balance in current)
            {
                if (previousDict.TryGetValue(balance.Asset, out var prev))
                {
                    if (prev.Free != balance.Free || prev.Locked != balance.Locked)
                    {
                        changes.Add(new BalanceChange
                        {
                            Asset = balance.Asset,
                            PreviousFree = prev.Free,
                            CurrentFree = balance.Free,
                            PreviousLocked = prev.Locked,
                            CurrentLocked = balance.Locked
                        });
                    }
                }
                else if (balance.Free > 0 || balance.Locked > 0)
                {
                    changes.Add(new BalanceChange
                    {
                        Asset = balance.Asset,
                        PreviousFree = 0,
                        CurrentFree = balance.Free,
                        PreviousLocked = 0,
                        CurrentLocked = balance.Locked
                    });
                }
            }

            return changes;
        }
    }

    /// <summary>
    /// Result of an account refresh operation
    /// </summary>
    public class AccountRefreshResult
    {
        public string ExchangeId { get; set; }
        public bool Success { get; set; }
        public string Error { get; set; }
        public DateTime RefreshedAt { get; set; }
        public List<ExchangeBalance> Balances { get; set; } = new List<ExchangeBalance>();
        public List<ExchangeSymbol> Symbols { get; set; } = new List<ExchangeSymbol>();
        public ExchangeFeeSchedule FeeSchedule { get; set; }
        public List<BalanceChange> BalanceChanges { get; set; } = new List<BalanceChange>();
    }

    /// <summary>
    /// Represents a change in balance between refresh cycles
    /// </summary>
    public class BalanceChange
    {
        public string Asset { get; set; }
        public decimal PreviousFree { get; set; }
        public decimal CurrentFree { get; set; }
        public decimal PreviousLocked { get; set; }
        public decimal CurrentLocked { get; set; }
        public decimal FreeDifference => CurrentFree - PreviousFree;
        public decimal LockedDifference => CurrentLocked - PreviousLocked;
    }
}
