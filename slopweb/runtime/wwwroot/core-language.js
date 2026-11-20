import {yieldStartupPaint} from './startup-yield.js';

const key = 'slopworld.language.v1';

export async function installCoreLanguage(probe, parameters, view = window, request = fetch) {
  const persist = parameters.get('localPreferences') !== 'off';
  let folder = parameters.get('language');
  if (folder === null && persist) {
    try { folder = view.localStorage.getItem(key); } catch { /* Storage can be disabled. */ }
  }
  const select = name => {
    const url = new URL(view.location.href);
    url.searchParams.set('language', name);
    if (persist) {
      try { view.localStorage.setItem(key, name); } catch { /* The URL retains this selection. */ }
    }
    if (url.href === view.location.href) view.location.reload();
    else view.location.replace(url.href);
  };
  const enableMenu = () => probe.EnableLanguageMenu(select);
  const pack = probe.ConfigureCoreLanguage(folder ?? '');
  if (!pack) return enableMenu;
  try {
    const response = await request('./assets/locales/' + encodeURIComponent(pack) + '.zip');
    if (!response.ok) throw new Error('HTTP ' + response.status);
    const bytes = new Uint8Array(await response.arrayBuffer());
    await yieldStartupPaint();
    probe.InstallCoreLanguage(bytes);
  } catch (error) {
    // Selection commits only after installation. Entry can use its native default
    // if the pack disappeared or a transfer failed; selecting it again retries.
    console.warn('Could not install Core language ' + pack, error);
  }
  return enableMenu;
}
