import { useEffect, useRef, useState } from 'react';
import type { ShellSnapshot } from '../../contracts/generated/shell-snapshot';
import { connectBridge, sendIntent } from './bridge';
import { ConfigErrorScreen } from './ConfigErrorScreen';
import { applyAction, applyFocusRequest, initialFocusState, type FocusState } from './focus';
import { Placeholder } from './Placeholder';

export function App() {
  const [snapshot, setSnapshot] = useState<ShellSnapshot | null>(null);
  const [focus, setFocus] = useState<FocusState>(initialFocusState);

  // Stan fokusu i lista kafelków w refach: Actions przychodzą spoza Reacta, a reduktor ma widzieć najnowszy stan.
  const focusRef = useRef<FocusState>(initialFocusState);
  const appsRef = useRef<ShellSnapshot['apps']>([]);

  useEffect(
    () =>
      connectBridge(
        (next) => {
          appsRef.current = next.apps;
          focusRef.current = applyFocusRequest(focusRef.current, next);
          setFocus(focusRef.current);
          setSnapshot(next);
        },
        (action) => {
          const result = applyAction(focusRef.current, appsRef.current, action);
          focusRef.current = result.state;
          setFocus(result.state);
          if (result.intent) sendIntent('launchApp', result.intent);
        },
      ),
    [],
  );

  if (snapshot === null) {
    // Przed pierwszym snapshotem: samo tło ground, bez migania treści.
    return <div className="screen" />;
  }

  switch (snapshot.screen) {
    case 'placeholder':
      return <Placeholder apps={snapshot.apps} focusedAppId={focus.focusedAppId} />;
    case 'configError':
      return <ConfigErrorScreen errors={snapshot.configErrors} />;
    default: {
      // Nowy ShellScreen bez przypadku tutaj nie skompiluje się (F16).
      const unreachable: never = snapshot.screen;
      return unreachable;
    }
  }
}
