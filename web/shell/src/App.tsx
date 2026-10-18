import { useEffect, useState } from 'react';
import type { ShellSnapshot } from '../../contracts/generated/shell-snapshot';
import { connectBridge } from './bridge';
import { ConfigErrorScreen } from './ConfigErrorScreen';
import { Placeholder } from './Placeholder';

export function App() {
  const [snapshot, setSnapshot] = useState<ShellSnapshot | null>(null);

  useEffect(() => connectBridge(setSnapshot), []);

  if (snapshot === null) {
    // Przed pierwszym snapshotem: samo tło ground, bez migania treści.
    return <div className="screen" />;
  }

  switch (snapshot.screen) {
    case 'placeholder':
      return <Placeholder />;
    case 'configError':
      return <ConfigErrorScreen errors={snapshot.configErrors} />;
  }
}
