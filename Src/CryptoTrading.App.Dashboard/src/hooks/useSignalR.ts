import { useState, useEffect, useCallback, useRef } from 'react';
import * as signalR from '@microsoft/signalr';

export interface SignalREvents {
  onNewCandle?: (candle: unknown) => void;
  onRegimeChange?: (regime: unknown) => void;
  onTradeSignal?: (signal: unknown) => void;
  onParameterUpdate?: (params: unknown) => void;
}

export function useSignalR(hubUrl: string = '/hubs/trading', events?: SignalREvents) {
  const [connected, setConnected] = useState(false);
  const connectionRef = useRef<signalR.HubConnection | null>(null);

  useEffect(() => {
    const connection = new signalR.HubConnectionBuilder()
      .withUrl(hubUrl)
      .withAutomaticReconnect()
      .build();

    connectionRef.current = connection;

    if (events?.onNewCandle) connection.on('NewCandle', events.onNewCandle);
    if (events?.onRegimeChange) connection.on('RegimeChange', events.onRegimeChange);
    if (events?.onTradeSignal) connection.on('TradeSignal', events.onTradeSignal);
    if (events?.onParameterUpdate) connection.on('ParameterUpdate', events.onParameterUpdate);

    connection.start()
      .then(() => setConnected(true))
      .catch((err) => console.error('SignalR connection failed:', err));

    connection.onreconnecting(() => setConnected(false));
    connection.onreconnected(() => setConnected(true));
    connection.onclose(() => setConnected(false));

    return () => {
      connection.stop();
    };
  }, [hubUrl]);

  const invoke = useCallback(async (method: string, ...args: unknown[]) => {
    if (connectionRef.current?.state === signalR.HubConnectionState.Connected) {
      return connectionRef.current.invoke(method, ...args);
    }
  }, []);

  return { connected, invoke };
}
