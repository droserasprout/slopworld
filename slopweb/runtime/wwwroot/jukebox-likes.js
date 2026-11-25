// One TOML document per origin, shared by both outputs and every workspace tab.
const key = 'slopworld.jukebox.likes.v1';

export function jukeboxLikes(view) {
  const read = () => view.localStorage.getItem(key) ?? '';
  function write(original, text) {
    // Serialize the read/modify/write across tabs. Every writer reads inside the lock.
    return view.navigator.locks.request(key, () => {
      const current = read();
      if (original === null) {
        const separator = current && !current.endsWith('\n') ? '\n' : '';
        view.localStorage.setItem(key, current + separator + text);
      } else {
        if (original !== current)
          throw new Error('History changed. Copy your edits and reopen the editor for the latest version.');
        view.localStorage.setItem(key, text);
      }
    });
  }
  return {read, write};
}

export function watchJukeboxLikes(view, changed, signal) {
  view.addEventListener('storage', event => {
    if ((event.key === key || event.key === null) && event.storageArea === view.localStorage)
      changed();
  }, {signal});
  view.addEventListener('pageshow', event => { if (event.persisted) changed(); }, {signal});
}
