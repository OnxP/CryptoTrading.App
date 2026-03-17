import { CandlestickChart } from '../components/CandlestickChart';
import { PerformancePanel } from '../components/PerformancePanel';
import { LiveStatePanel } from '../components/LiveStatePanel';
import { useChartData } from '../hooks/useChartData';

export function Dashboard() {
  const { candles, loading } = useChartData('BTCUSDT', '4h');

  return (
    <div className="min-h-screen bg-gray-900 text-white">
      <header className="bg-gray-800 border-b border-gray-700 px-6 py-3">
        <h1 className="text-xl font-bold">CryptoTrading Dashboard</h1>
        <span className="text-gray-400 text-sm">BTCUSDT | 4H Regime Strategy</span>
      </header>

      <main className="p-4 grid grid-cols-12 gap-4">
        {/* Chart area */}
        <div className="col-span-9">
          {loading ? (
            <div className="flex items-center justify-center h-96 bg-gray-800 rounded-lg">
              <span className="text-gray-400">Loading chart data...</span>
            </div>
          ) : (
            <CandlestickChart candles={candles} width={1000} height={500} />
          )}
        </div>

        {/* Side panels */}
        <div className="col-span-3 space-y-4">
          <LiveStatePanel />
          <PerformancePanel />
        </div>
      </main>
    </div>
  );
}
