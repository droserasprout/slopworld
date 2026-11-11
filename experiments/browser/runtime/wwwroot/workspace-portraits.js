// Optional locally exported appearance assets. A missing export invents no identity.
export async function loadWorkspacePortraits(managed, fetchAsset = fetch) {
  const response = await fetchAsset('./assets/portraits/manifest.json');
  if (response.status === 404) return 0;
  if (!response.ok) throw new Error('Portrait manifest could not be loaded');
  const manifest = await response.json();
  if (manifest.version !== 1 || !Array.isArray(manifest.entries) || manifest.entries.length > 1024)
    throw new Error('Invalid portrait inventory');
  const names = new Set();
  let total = 0;
  for (const entry of manifest.entries) {
    if (!entry || typeof entry.session !== 'string' || !entry.session || entry.session.length > 256 || names.has(entry.session)
        || !/^[0-9a-f]{64}\.rgba$/.test(entry.file) || !Number.isInteger(entry.width) || !Number.isInteger(entry.height)
        || entry.width < 1 || entry.height < 1 || entry.width > 2048 || entry.height > 2048)
      throw new Error('Invalid portrait identity or dimensions');
    names.add(entry.session);
    const size = 8 + entry.width * entry.height * 4;
    total += size;
    if (total > 64 * 1024 * 1024) throw new Error('Portrait inventory exceeds its pixel bound');
    const image = await fetchAsset('./assets/portraits/' + entry.file);
    if (!image.ok) throw new Error('Portrait pixels are missing');
    const data = await image.arrayBuffer();
    if (data.byteLength !== size) throw new Error('Portrait pixels are truncated');
    const header = new DataView(data);
    if (header.getInt32(0, true) !== entry.width || header.getInt32(4, true) !== entry.height)
      throw new Error('Portrait dimensions disagree');
    const digest = new Uint8Array(await crypto.subtle.digest('SHA-256', data));
    const hash = Array.from(digest, byte => byte.toString(16).padStart(2,'0')).join('');
    if (hash + '.rgba' !== entry.file) throw new Error('Portrait pixels changed');
    managed.WorkspacePortrait(entry.session, new Uint8Array(data));
  }
  return names.size;
}
