import { useState, useEffect } from 'react';
import axios from 'axios';

interface LiveState {
  regime: string;
  volatility: string;
  openTrades: number;
  lastUpdate: string | null;
}

export function LiveStatePanel() {
  const [state, setState] = useState<LiveState | null>(null);

  useEffect(() => {
    const fetch = () => {
      axios.get('/api/live/state')
        .then(res => setState(res.data))
        .catch(err => console.error('Failed to load live state:', err));
    };
    fetch();
    const interval = setInterval(fetch, 10000); // Refresh every 10s
    return () => clearInterval(interval);
  }, []);

  const regimeColor = (regime: string) => {
    switch (regime) {
      case 'BullMarket': return 'text-green-400';
      case 'BearMarket': return 'text-red-400';
      default: return 'text-gray-400';
    }
  };

  if (!state) return <div className="p-4 text-gray-400">Loading...</div>;

  return (
    <div className="bg-gray-800 rounded-lg p-4">
      <h3 className="text-lg font-semibold text-white mb-3">Live State</h3>
      <div className="space-y-2 text-sm">
        <div className="flex justify-between">
          <span className="text-gray-400">Regime</span>
          <span className={regimeColor(state.regime)}>{state.regime}</span>
        </div>
        <div className="flex justify-between">
          <span className="text-gray-400">Volatility</span>
          <span className="text-white">{state.volatility}</span>
        </div>
        <div className="flex justify-between">
          <span className="text-gray-400">Open Trades</span>
          <span className="text-white">{state.openTrades}</span>
        </div>
        <div className="flex justify-between">
          <span className="text-gray-400">Last Update</span>
          <span className="text-gray-300 text-xs">{state.lastUpdate || 'N/A'}</span>
        </div>
      </div>
    </div>
  );
}
