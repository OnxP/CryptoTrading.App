using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CryptoTrading.App.Persistence;
using CryptoTrading.App.Persistence.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace CryptoTrading.App.Algorithm.DualRegime.Persistence
{
    public class AlgoStatePersistence
    {
        private readonly CryptoDbContextPg _db;
        private readonly ILogger<AlgoStatePersistence> _logger;
        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        };

        public AlgoStatePersistence(CryptoDbContextPg db, ILogger<AlgoStatePersistence> logger)
        {
            _db = db;
            _logger = logger;
        }

        public async Task SaveAsync(string strategyName, string instanceId, DualRegimeStateSnapshot snapshot)
        {
            try
            {
                var json = JsonSerializer.Serialize(snapshot, JsonOptions);

                var existing = await _db.AlgoStates
                    .FirstOrDefaultAsync(s => s.StrategyName == strategyName && s.InstanceId == instanceId);

                if (existing != null)
                {
                    existing.StateJson = json;
                    existing.LastBarTime = snapshot.LastBarTime;
                    existing.Last1hBarCount = snapshot.Aggregator?.Bars1H?.Count ?? 0;
                    existing.Last4hBarCount = snapshot.Aggregator?.Bars4H?.Count ?? 0;
                    existing.UpdatedAt = DateTime.UtcNow;
                }
                else
                {
                    _db.AlgoStates.Add(new AlgoStateEntity
                    {
                        StrategyName = strategyName,
                        InstanceId = instanceId,
                        StateJson = json,
                        LastBarTime = snapshot.LastBarTime,
                        Last1hBarCount = snapshot.Aggregator?.Bars1H?.Count ?? 0,
                        Last4hBarCount = snapshot.Aggregator?.Bars4H?.Count ?? 0,
                        UpdatedAt = DateTime.UtcNow
                    });
                }

                await _db.SaveChangesAsync();
                _logger.LogDebug("Saved algo state for {Strategy}/{Instance}", strategyName, instanceId);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save algo state for {Strategy}/{Instance}", strategyName, instanceId);
            }
        }

        public async Task<DualRegimeStateSnapshot> LoadAsync(string strategyName, string instanceId)
        {
            var entity = await _db.AlgoStates
                .FirstOrDefaultAsync(s => s.StrategyName == strategyName && s.InstanceId == instanceId);

            if (entity == null)
                return null;

            try
            {
                return JsonSerializer.Deserialize<DualRegimeStateSnapshot>(entity.StateJson, JsonOptions);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to deserialize algo state for {Strategy}/{Instance}", strategyName, instanceId);
                return null;
            }
        }

        public async Task<TimeSpan?> GetGapDurationAsync(string strategyName, string instanceId)
        {
            var entity = await _db.AlgoStates
                .FirstOrDefaultAsync(s => s.StrategyName == strategyName && s.InstanceId == instanceId);

            if (entity == null)
                return null;

            return DateTime.UtcNow - entity.LastBarTime;
        }

        public async Task<ModelArtifactEntity> LoadActiveModelAsync(string strategyName)
        {
            return await _db.ModelArtifacts
                .Where(m => m.StrategyName == strategyName && m.IsActive)
                .OrderByDescending(m => m.Version)
                .FirstOrDefaultAsync();
        }

        public async Task<int?> GetActiveModelVersionAsync(string strategyName)
        {
            return await _db.ModelArtifacts
                .Where(m => m.StrategyName == strategyName && m.IsActive)
                .MaxAsync(m => (int?)m.Version);
        }
    }
}
