import { useState, useEffect } from 'react';
import axios from 'axios';

interface PerformanceMetrics {
  totalTrades: number;
  winRate: number;
  totalReturn: number;
  sharpeRatio: number;
  maxDrawdown: number;
  profitFactor: number;
}

export function PerformancePanel() {
  const [metrics, setMetrics] = useState<PerformanceMetrics | null>(null);

  useEffect(() => {
    axios.get('/api/performance/summary')
      .then(res => setMetrics(res.data))
      .catch(err => console.error('Failed to load performance:', err));
  }, []);

  if (!metrics) return <div className="p-4 text-gray-400">Loading metrics...</div>;

  return (
    <div className="bg-gray-800 rounded-lg p-4">
      <h3 className="text-lg font-semibold text-white mb-3">Performance</h3>
      <div className="grid grid-cols-2 gap-3 text-sm">
        <MetricRow label="Total Trades" value={metrics.totalTrades.toString()} />
        <MetricRow label="Win Rate" value={`${(metrics.winRate * 100).toFixed(1)}%`}
          color={metrics.winRate >= 0.5 ? 'text-green-400' : 'text-red-400'} />
        <MetricRow label="Total Return" value={`$${metrics.totalReturn.toFixed(2)}`}
          color={metrics.totalReturn >= 0 ? 'text-green-400' : 'text-red-400'} />
        <MetricRow label="Sharpe Ratio" value={metrics.sharpeRatio.toFixed(2)} />
        <MetricRow label="Max Drawdown" value={`$${metrics.maxDrawdown.toFixed(2)}`} color="text-red-400" />
        <MetricRow label="Profit Factor" value={metrics.profitFactor.toFixed(2)}
          color={metrics.profitFactor >= 1 ? 'text-green-400' : 'text-red-400'} />
      </div>
    </div>
  );
}

function MetricRow({ label, value, color = 'text-white' }: { label: string; value: string; color?: string }) {
  return (
    <div className="flex justify-between">
      <span className="text-gray-400">{label}</span>
      <span className={color}>{value}</span>
    </div>
  );
}
