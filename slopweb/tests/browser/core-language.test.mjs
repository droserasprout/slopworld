import test from 'node:test';
import assert from 'node:assert/strict';
import {installCoreLanguage} from '../../runtime/wwwroot/core-language.js';

function fixture({saved = null, deny = false, unavailable = false, nativeDefault = 'English'} = {}) {
  const calls = [], installed = [], redirects = [];
  let reloads = 0;
  const view = {
    location: {href: 'https://host/entry?session=one', replace(url) { this.href = url; redirects.push(new URL(url)); }, reload() { reloads++; }},
    localStorage: {
      getItem() { if (deny) throw Error(); return saved; },
      setItem(_key, value) { if (deny) throw Error(); saved = value; }
    }
  };
  let switchLanguage;
  const probe = {
    EnableLanguageMenu(select) { switchLanguage = select; },
    ConfigureCoreLanguage(folder) {
      const selected = folder || nativeDefault;
      return ['French (Français)', 'Loose'].includes(selected) ? selected : '';
    },
    InstallCoreLanguage(bytes) { installed.push(...bytes); }
  };
  const request = async url => {
    calls.push(url);
    return {ok: !unavailable, status: 404, arrayBuffer: async () => Uint8Array.of(42).buffer};
  };
  return {
    async start(query) { (await installCoreLanguage(probe, new URLSearchParams(query), view, request))(); },
    select: folder => switchLanguage(folder), calls, installed, redirects, saved: () => saved, reloads: () => reloads
  };
}

test('startup fetches only the selected locale and preserves native default and stale-name fallback', async () => {
  for (const query of ['', 'language=English', 'language=Removed', 'language=../escape']) {
    const f = fixture();
    await f.start(query);
    assert.deepEqual(f.calls, []);
  }
  const f = fixture({saved: 'Loose'});
  await f.start('language=French (Français)');
  assert.deepEqual(f.calls, ['./assets/locales/French%20(Fran%C3%A7ais).zip']);
  assert.deepEqual(f.installed, [42]);
});

test('menu switches retain workspace parameters and survive denied storage and disabled persistence', async () => {
  for (const deny of [false,true]) {
    const f = fixture({saved: 'Loose', deny});
    await f.start('');
    assert.equal(f.calls.length, deny ? 0 : 1);
    f.select('French (Français)');
    assert.equal(f.redirects[0].searchParams.get('language'), 'French (Français)');
    assert.equal(f.redirects[0].searchParams.get('session'), 'one');
    assert.equal(f.saved(), deny ? 'Loose' : 'French (Français)');
  }
  const f = fixture({saved: 'Loose'});
  await f.start('localPreferences=off');
  f.select('English');
  assert.deepEqual(f.calls, []);
  assert.equal(f.saved(), 'Loose');
  assert.equal(f.redirects[0].searchParams.get('language'), 'English');
});

test('unavailable language pack allows Entry to use its default and retry on reload', async () => {
  const f = fixture({unavailable:true});
  await f.start('language=Loose');
  assert.deepEqual(f.installed, []);
  f.select('Loose');
  assert.equal(f.redirects.length, 1);
  f.select('Loose');
  assert.equal(f.reloads(), 1);
});

test('a packed native default loads with no language override', async () => {
  const f = fixture({nativeDefault:'Loose'});
  await f.start('');
  assert.deepEqual(f.calls, ['./assets/locales/Loose.zip']);
  assert.deepEqual(f.installed, [42]);
});
