// One browser decoder for authored OST. Native Radio supplies the permission to
// play after its daemon stop acknowledgment; autoplay rejection waits for input.
export function attachWorkspaceAudio(managed, audio = new Audio(), target = window) {
  audio.preload = 'none';
  let identity = '', requested = false, pending = false, blocked = false, disposed = false, generation = 0;
  const report = () => managed.WorkspaceAudioPlaying(!audio.paused && !audio.ended);
  const play = () => {
    if (!requested || pending || disposed || !audio.paused) return;
    pending = true;
    const run = generation;
    Promise.resolve(audio.play()).then(() => {
      if (disposed || run !== generation || !requested) { if (!requested || disposed) audio.pause(); return; }
      blocked = false; report();
    }, () => { if (run === generation) { blocked = true; managed.WorkspaceAudioPlaying(false); } })
      .finally(() => { if (run === generation) pending = false; });
  };
  const ended = () => {
    if (!requested || disposed || !audio.ended) return;
    managed.WorkspaceAudioPlaying(false); managed.WorkspaceAudioEnded(); frame();
  };
  const failed = () => { blocked = true; audio.pause(); managed.WorkspaceAudioPlaying(false); };
  const retry = () => { if (blocked) { blocked = false; play(); } };
  const frame = () => {
    const state = JSON.parse(managed.WorkspaceAudio());
    if (!/^[a-z]+-\d{8}$/.test(state.track)) throw new Error('Invalid OST asset');
    requested = state.play;
    audio.volume = Math.max(0, Math.min(1, state.volume));
    if (!requested) {
      if (identity) {
        generation++; pending = false; blocked = false; identity = '';
        audio.pause(); audio.removeAttribute('src'); audio.load();
        managed.WorkspaceAudioPlaying(false);
      }
      return;
    }
    const next = `${state.track}:${state.revision}`;
    if (next !== identity) {
      generation++; pending = false; blocked = false; audio.pause();
      audio.src = new URL(`./assets/ost/${state.track}.ogg`, import.meta.url).href;
      identity = next; managed.WorkspaceAudioPlaying(false);
    }
    if (!blocked) play();
  };
  audio.addEventListener('ended', ended);
  audio.addEventListener('playing', report);
  audio.addEventListener('pause', report);
  audio.addEventListener('error', failed);
  target.addEventListener('pointerdown', retry);
  target.addEventListener('keydown', retry);
  return {frame, dispose() {
    disposed = true; generation++; audio.pause(); audio.removeAttribute('src'); audio.load();
    audio.removeEventListener('ended', ended); audio.removeEventListener('playing', report); audio.removeEventListener('pause', report); audio.removeEventListener('error', failed);
    target.removeEventListener('pointerdown', retry); target.removeEventListener('keydown', retry);
    managed.WorkspaceAudioPlaying(false);
  }};
}
