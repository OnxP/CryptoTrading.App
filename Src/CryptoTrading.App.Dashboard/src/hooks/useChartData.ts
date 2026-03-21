import { useState, useEffect } from 'react';
import axios from 'axios';

export interface CandlestickData {
  time: number;
  open: number;
  high: number;
  low: number;
  close: number;
  volume: number;
}

export interface RegimeZone {
  start: number;
  end: number;
  regime: 'Bull' | 'Bear' | 'Ranging';
}

export interface TradeSignal {
  time: number;
  side: 'Buy' | 'Sell';
  price: number;
  type: 'Entry' | 'Exit';
}

export interface SupplyDemandZone {
  high: number;
  low: number;
  type: 'Supply' | 'Demand';
  touches: number;
}

export function useChartData(symbol: string, interval: string = '4h') {
  const [candles, setCandles] = useState<CandlestickData[]>([]);
  const [regimes, setRegimes] = useState<RegimeZone[]>([]);
  const [signals, setSignals] = useState<TradeSignal[]>([]);
  const [zones, setZones] = useState<SupplyDemandZone[]>([]);
  const [loading, setLoading] = useState(true);

  useEffect(() => {
    const fetchData = async () => {
      setLoading(true);
      try {
        const [candleRes, regimeRes, signalRes, zoneRes] = await Promise.all([
          axios.get(`/api/chart/${symbol}/candles?interval=${interval}`),
          axios.get(`/api/chart/${symbol}/regimes`),
          axios.get(`/api/chart/${symbol}/signals`),
          axios.get(`/api/chart/${symbol}/zones`),
        ]);
        setCandles(candleRes.data.candles || []);
        setRegimes(regimeRes.data.regimes || []);
        setSignals(signalRes.data.signals || []);
        setZones(zoneRes.data.zones || []);
      } catch (error) {
        console.error('Failed to fetch chart data:', error);
      } finally {
        setLoading(false);
      }
    };
    fetchData();
  }, [symbol, interval]);

  return { candles, regimes, signals, zones, loading };
}
