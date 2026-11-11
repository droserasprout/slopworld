// Observe packets on their ordinary path to Canvas; do not consume uploads or
// inject terminal input. Restore the export even when the observation times out.
(async () => {
  const managed = window.__managedProbe;
  if (window.__workspace?.status !== 'running') throw new Error('Workspace is not running');
  const original = managed.WorkspaceUpdate;
  const frames = [], fallbacks = new Set();
  try {
    managed.WorkspaceUpdate = (...args) => {
      const packet = original(...args), parsed = JSON.parse(packet), kinds = {};
      for (const command of parsed.commands) {
        kinds[command.kind] = (kinds[command.kind] ?? 0) + 1;
        if (command.kind === 'text' && fallbacks.size < 16)
          fallbacks.add(JSON.stringify({text: command.value.text, size: command.value.fontSize, style: command.value.style}));
      }
      if (frames.length < 60) frames.push(kinds);
      return packet;
    };
    if (managed.WorkspaceUpdate === original) throw new Error('Managed exports cannot be observed');
    const until = performance.now() + 15000;
    while (frames.length < 60 && performance.now() < until)
      await new Promise(resolve => setTimeout(resolve, 100));
    if (!frames.length) throw new Error('No workspace frames observed');
    const packets = new Map();
    for (const frame of frames) {
      const key = JSON.stringify(frame);
      packets.set(key, (packets.get(key) ?? 0) + 1);
    }
    return {frameCount: frames.length,
      packets: [...packets].map(([frame, count]) => ({count, kinds: JSON.parse(frame)})),
      genericTextSamples: [...fallbacks].map(JSON.parse), state: window.__workspace.status};
  } finally {
    managed.WorkspaceUpdate = original;
  }
})()
