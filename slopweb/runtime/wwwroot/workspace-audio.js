// Each attempt owns a decoder, a relay listener and its metadata request. Never
// reuse a decoder across URLs: media events do not identify their original src.
function browserName(agent = '') {
  if (/Edg(?:e|A|iOS)?\//.test(agent)) return 'Edge';
  if (/OPR\//.test(agent)) return 'Opera';
  if (/Firefox\/|FxiOS\//.test(agent)) return 'Firefox';
  if (/Chrome\/|CriOS\//.test(agent)) return 'Chrome';
  if (/Safari\//.test(agent)) return 'Safari';
  return 'Browser';
}

export function attachWorkspaceAudio(managed, {
  createAudio = () => new Audio(),
  target = window,
  fetchTitle = globalThis.fetch,
} = {}) {
  managed.WorkspaceAudioIdentify(browserName(target.navigator?.userAgent));

  let state, encoded, revision;
  let attempt, gesture, resumeIndex;
  let suspended = false, disposed = false;
  const report = (playing, stream = '', title = '', error = '') =>
    managed.WorkspaceAudioReport(revision, playing, stream, title, error);

  function release() {
    if (!attempt) return;
    const {audio, cancel, timer} = attempt;
    attempt = null;
    cancel.abort();
    clearTimeout(timer);
    audio.pause();
    audio.removeAttribute('src');
    audio.load(); // Abort the media request and release the browser's encoded buffers.
  }

  async function pollTitle(run) {
    try {
      const query = new URLSearchParams({listener: run.listener});
      const response = await fetchTitle('./host/media/radio/title?' + query, {
        signal: run.cancel.signal, cache: 'no-store',
      });
      if (response.ok) {
        const {title} = await response.json();
        const nextTitle = typeof title === 'string' ? title : '';
        if (attempt === run && nextTitle !== run.title) {
          run.title = nextTitle;
          report(!run.audio.paused, run.stream, run.title);
        }
      }
    } catch { /* The title is optional, including when the listener is closing. */ }
    if (attempt === run) run.timer = setTimeout(() => pollTitle(run), 3000);
  }

  function fail(run, unsupported, reason) {
    if (attempt !== run) return;
    release();
    if (unsupported && state.station && run.index + 1 < state.streams.length) {
      start(run.index + 1);
      return;
    }
    report(false, '', '', reason);
  }

  function start(index) {
    const stream = state.streams[index] ?? '';
    const radio = Boolean(state.station);
    if (radio && !stream) {
      report(false, '', '', 'This station has no available streams. Choose another station.');
      return;
    }
    if (!radio && !/^[\w-]+$/.test(state.track)) {
      report(false, '', '', 'Invalid soundtrack track. Choose another track.');
      return;
    }
    const run = {
      audio: createAudio(),
      cancel: new AbortController(),
      listener: radio ? crypto.randomUUID() : '',
      index, stream, title: '', polling: false,
    };
    attempt = run;
    const audio = run.audio;
    audio.preload = 'none';
    audio.volume = state.volume;
    const options = {signal: run.cancel.signal};
    const unsupported = radio
      ? 'This browser could not decode any stream for this station. Choose another station.'
      : 'This browser could not decode the soundtrack. Try a browser with Ogg Vorbis support.';
    audio.addEventListener('error', () => {
      const code = audio.error?.code;
      const format = code === 3 || code === 4;
      fail(run, format, format ? unsupported : 'Audio stream unavailable. Check the connection and press Play to retry.');
    }, options);
    audio.addEventListener('ended', () => {
      if (attempt !== run) return;
      if (radio) {
        fail(run, false, 'The live stream ended. Press Play to reconnect.');
      } else {
        release();
        managed.WorkspaceAudioEnded(revision);
        frame();
      }
    }, options);
    function playing() {
      if (attempt !== run || audio.paused) return;
      report(true, stream, run.title);
      if (radio && !run.polling) {
        run.polling = true;
        void pollTitle(run);
      }
    }
    audio.addEventListener('playing', playing, options);
    audio.addEventListener('pause', () => {
      if (attempt !== run || !audio.paused || audio.ended) return;
      // The OS can pause playback independently of the jukebox controls.
      fail(run, false, 'Browser playback paused. Press Play to resume.');
    }, options);
    audio.src = radio
      ? './host/media/radio?' + new URLSearchParams({station: state.station, stream, listener: run.listener})
      : new URL('./assets/ost/' + state.track + '.ogg', import.meta.url).href;
    report(false, stream);
    audio.play().then(() => {
      if (attempt === run) playing();
      else audio.pause();
    }, error => {
      if (attempt !== run) return;
      if (error.name === 'NotAllowedError') {
        release();
        gesture = index;
        report(false, '', '', 'Click or press a key to enable browser audio.');
      } else {
        const format = error.name === 'NotSupportedError';
        fail(run, format, format ? unsupported : 'Browser playback failed. Press Play to retry.');
      }
    });
  }

  function frame() {
    if (disposed || suspended) return;
    const next = managed.WorkspaceAudio();
    if (encoded === next) return;
    encoded = next;
    state = JSON.parse(next);
    if (state.revision !== revision) {
      release();
      gesture = undefined;
      revision = state.revision;
      if (state.play) start(0);
    } else if (attempt) {
      attempt.audio.volume = state.volume;
    }
  }

  const events = new AbortController();
  const options = {signal: events.signal};
  function retry() {
    if (gesture === undefined || suspended) return;
    frame(); // A new managed command may already have superseded the denied one.
    if (gesture !== undefined) {
      const index = gesture;
      gesture = undefined;
      start(index);
    }
  }
  for (const event of ['pointerdown', 'pointerup', 'keydown'])
    target.addEventListener(event, retry, options);

  // A frozen page may return from the back/forward cache. Release the live
  // connection while frozen and reconnect with a new listener on restoration.
  target.addEventListener('pagehide', event => {
    if (!event.persisted) return;
    suspended = true;
    resumeIndex = attempt?.index;
    if (attempt) {
      release();
      report(false);
    }
  }, options);
  target.addEventListener('pageshow', () => {
    if (!suspended) return;
    suspended = false;
    const previous = revision;
    frame();
    if (revision === previous && resumeIndex !== undefined) start(resumeIndex);
    resumeIndex = undefined;
  }, options);

  return {frame, dispose(orderly = true) {
    if (disposed) return;
    disposed = true;
    events.abort();
    release();
    if (state?.play) report(false);
    if (orderly) managed.WorkspaceAudioQuit();
  }};
}
