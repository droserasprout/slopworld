//! Durable source identities, coverage snapshots and immutable font objects.
//! Fontconfig owns enumeration; callers hold the operation lock through publication.
use super::{
    fontconfig,
    opentype::{self, Face},
};
use anyhow::{Context, Result, ensure};
use serde::{Deserialize, Serialize};
use sha2::{Digest, Sha256};
use std::{
    collections::{BTreeMap, HashSet},
    fs,
    io::Write,
    os::unix::fs::MetadataExt,
    path::{Path, PathBuf},
    time::Instant,
};

// Bump when persisted discovery or fallback policy changes.
const SCHEMA: u32 = 2;

#[cfg(test)]
#[path = "cache_tests.rs"]
mod tests;
#[derive(Clone, Debug, Default, Serialize, Deserialize, PartialEq, Eq)]
pub(super) struct Stamp {
    len: u64,
    mtime: i64,
    mtime_ns: i64,
    ctime: i64,
    ctime_ns: i64,
    inode: u64,
}
pub(super) fn stamp(path: &Path) -> Option<Stamp> {
    fs::metadata(path).ok().map(|m| Stamp {
        len: m.len(),
        mtime: m.mtime(),
        mtime_ns: m.mtime_nsec(),
        ctime: m.ctime(),
        ctime_ns: m.ctime_nsec(),
        inode: m.ino(),
    })
}
#[derive(Clone, Serialize, Deserialize)]
struct SourceFace {
    index: u32,
    face: Face,
}
#[derive(Clone, Serialize, Deserialize)]
struct Source {
    stamp: Stamp,
    raw: String,
    faces: Vec<SourceFace>,
}
#[derive(Default, Serialize, Deserialize)]
pub(super) struct Catalog {
    schema: u32,
    environment: String,
    watches: BTreeMap<PathBuf, Option<Stamp>>,
    sources: BTreeMap<PathBuf, Source>,
    pub faces: Vec<Face>,
    pub chains: BTreeMap<String, Vec<String>>,
    pub generation: String,
}
pub(super) fn digest(bytes: &[u8]) -> String {
    format!("{:x}", Sha256::digest(bytes))
}
pub(super) fn write(path: &Path, bytes: &[u8]) -> Result<()> {
    let parent = path.parent().context("Cache path has no parent")?;
    fs::create_dir_all(parent)?;
    let mut temporary = tempfile::NamedTempFile::new_in(parent)?;
    temporary.write_all(bytes)?;
    temporary.as_file().sync_all()?;
    temporary.persist(path).map_err(|e| e.error)?;
    Ok(())
}
pub(super) fn object(root: &Path, hash: &str) -> PathBuf {
    root.join("objects").join(format!("{hash}.ttf"))
}
fn environment() -> String {
    let names = [
        "HOME",
        "XDG_CONFIG_HOME",
        "XDG_DATA_HOME",
        "XDG_CACHE_HOME",
        "FONTCONFIG_FILE",
        "FONTCONFIG_PATH",
        "FONTCONFIG_SYSROOT",
        "LANG",
        "LC_ALL",
        "LC_CTYPE",
    ];
    digest(
        &serde_json::to_vec(&names.map(|name| {
            (
                name,
                std::env::var_os(name).map(|s| s.to_string_lossy().into_owned()),
            )
        }))
        .unwrap(),
    )
}
impl Catalog {
    pub fn load(root: &Path) -> Self {
        fs::read(root.join("catalog-v1.json"))
            .ok()
            .and_then(|b| serde_json::from_slice(&b).ok())
            .unwrap_or_default()
    }
    pub fn save(&self, root: &Path) -> Result<()> {
        write(&root.join("catalog-v1.json"), &serde_json::to_vec(self)?)
    }
    pub fn fresh(&self, root: &Path) -> bool {
        self.schema == SCHEMA
            && self.environment == environment()
            && !self.faces.is_empty()
            && self.watches.iter().all(|(path, old)| stamp(path) == *old)
            && self
                .faces
                .iter()
                .all(|face| object(root, &face.hash).is_file())
    }
    pub fn rebuild(&mut self, root: &Path) -> Result<()> {
        let (files, watch) = fontconfig::inventory()?;
        self.rebuild_inventory(root, files, watch)
    }
    pub(super) fn rebuild_inventory(
        &mut self,
        root: &Path,
        files: Vec<PathBuf>,
        mut watch: Vec<PathBuf>,
    ) -> Result<()> {
        let started = Instant::now();
        let mut sources = BTreeMap::new();
        let mut parsed = 0;
        for path in files {
            let Some(current) = stamp(&path) else {
                continue;
            };
            if let Some(previous) = self.sources.get(&path).filter(|s| s.stamp == current) {
                sources.insert(path, previous.clone());
                continue;
            }
            let Ok(bytes) = fs::read(&path) else { continue };
            parsed += 1;
            let count = ttf_parser::fonts_in_collection(&bytes)
                .unwrap_or(1)
                .min(4096);
            let faces = (0..count)
                .filter_map(|index| {
                    opentype::metadata(&bytes, index, false)
                        .ok()
                        .map(|face| SourceFace { index, face })
                })
                .collect();
            sources.insert(
                path,
                Source {
                    stamp: current,
                    raw: String::new(),
                    faces,
                },
            );
        }
        // Match native path order and strict score improvement: ties retain the
        // first path/face, not the order supplied by Fontconfig's match operation.
        let mut paths: Vec<_> = sources.keys().cloned().collect();
        paths.sort_by_key(|p| p.to_string_lossy().to_lowercase());
        let mut selected: BTreeMap<String, (PathBuf, usize, i32)> = BTreeMap::new();
        for path in &paths {
            for (i, entry) in sources[path].faces.iter().enumerate() {
                let key = entry.face.family.to_lowercase();
                if selected
                    .get(&key)
                    .is_none_or(|(_, _, score)| entry.face.score < *score)
                {
                    selected.insert(key, (path.clone(), i, entry.face.score));
                }
            }
        }
        let mut faces = Vec::new();
        for (path, index, _) in selected.values() {
            let source = sources.get_mut(path).unwrap();
            if source.raw.is_empty() || !root.join("raw").join(&source.raw).is_file() {
                let raw = fs::read(path)?;
                source.raw = digest(&raw);
                write(&root.join("raw").join(&source.raw), &raw)?;
            }
            let entry = &mut source.faces[*index];
            if entry.face.hash.is_empty() || !object(root, &entry.face.hash).is_file() {
                let raw = fs::read(root.join("raw").join(&source.raw))?;
                let Ok(bytes) = opentype::extract(&raw, entry.index) else {
                    continue;
                };
                let Ok(mut face) = opentype::metadata(&bytes, 0, true) else {
                    continue;
                };
                face.hash = digest(&bytes);
                write(&object(root, &face.hash), &bytes)?;
                entry.face = face;
            }
            faces.push(entry.face.clone());
        }
        ensure!(
            !faces.is_empty(),
            "No supported installed font families found"
        );
        ensure!(
            sources
                .iter()
                .all(|(path, source)| stamp(path).as_ref() == Some(&source.stamp)),
            "Installed fonts changed during discovery; retry preparation"
        );
        watch.extend(paths);
        // Directory stamps detect new and removed files; source stamps also catch
        // replacement in place. Watch parents of absent configured directories.
        let mut expanded = Vec::new();
        for path in watch {
            watch_tree(&path, &mut expanded);
            if let Some(parent) = path.parent() {
                expanded.push(parent.to_owned());
            }
        }
        let watches = expanded
            .into_iter()
            .map(|p| {
                let s = stamp(&p);
                (p, s)
            })
            .collect();
        let environment = environment();
        let generation = digest(&serde_json::to_vec(&(SCHEMA, &environment, &watches))?);
        *self = Self {
            schema: SCHEMA,
            environment,
            watches,
            sources,
            faces,
            chains: BTreeMap::new(),
            generation,
        };
        self.save(root)?;
        self.prune(root)?;
        eprintln!(
            "slopweb fonts: indexed {} families ({} changed sources) in {:.3}s",
            self.faces.len(),
            parsed,
            started.elapsed().as_secs_f64()
        );
        Ok(())
    }
    fn prune(&self, root: &Path) -> Result<()> {
        let objects = self
            .faces
            .iter()
            .map(|f| format!("{}.ttf", f.hash))
            .collect();
        let raw = self
            .sources
            .values()
            .filter(|s| !s.raw.is_empty())
            .map(|s| s.raw.clone())
            .collect();
        prune_stale(&root.join("objects"), &objects, 64, 64 * 1024 * 1024)?;
        prune_stale(&root.join("raw"), &raw, 64, 64 * 1024 * 1024)
    }
    pub fn restore_object(&self, root: &Path, hash: &str) -> Result<()> {
        for (path, source) in &self.sources {
            if let Some(entry) = source.faces.iter().find(|entry| entry.face.hash == hash) {
                let raw_path = root.join("raw").join(&source.raw);
                let raw = fs::read(&raw_path)
                    .ok()
                    .filter(|b| digest(b) == source.raw)
                    .map(Ok)
                    .unwrap_or_else(|| fs::read(path))?;
                ensure!(digest(&raw) == source.raw, "Original font identity changed");
                let bytes = opentype::extract(&raw, entry.index)?;
                ensure!(digest(&bytes) == hash, "Extracted font identity changed");
                write(&raw_path, &raw)?;
                return write(&object(root, hash), &bytes);
            }
        }
        anyhow::bail!("Font content is no longer in the installed catalog")
    }
    pub fn chain(&mut self, root: &Path, family: &str) -> Result<Vec<String>> {
        self.chain_with_order(root, family, fontconfig::order)
    }
    fn chain_with_order(
        &mut self,
        root: &Path,
        family: &str,
        discover: impl FnOnce(&str) -> Result<Vec<String>>,
    ) -> Result<Vec<String>> {
        if let Some(chain) = self.chains.get(family) {
            return Ok(chain.clone());
        }
        let names: BTreeMap<_, _> = self
            .faces
            .iter()
            .map(|f| (f.family.to_lowercase(), f.family.clone()))
            .collect();
        let mut order = Vec::new();
        let mut seen = std::collections::HashSet::new();
        // Fontconfig already supplies the complete ordered monochrome fallback
        // set. Catalog-only families may be color fonts that it excluded.
        for name in discover(family)? {
            if let Some(canonical) = names.get(&name.to_lowercase())
                && seen.insert(canonical.to_lowercase())
            {
                order.push(canonical.clone());
            }
        }
        // Bound persisted requested-family keys while allowing the complete catalog.
        if self.chains.len() >= 256 {
            self.chains.clear();
        }
        self.chains.insert(family.to_owned(), order.clone());
        self.save(root)?;
        Ok(order)
    }
}
/// Current catalog references are never evicted. Retired data retains a bounded
/// grace cache for clients holding an older catalog during a live font change.
pub(super) fn prune_stale(
    directory: &Path,
    live: &HashSet<String>,
    max_count: usize,
    max_bytes: u64,
) -> Result<()> {
    let Ok(entries) = fs::read_dir(directory) else {
        return Ok(());
    };
    let mut stale = Vec::new();
    for entry in entries {
        let entry = entry?;
        if live.contains(&entry.file_name().to_string_lossy().into_owned()) {
            continue;
        }
        let metadata = entry.metadata()?;
        if metadata.is_file() {
            stale.push((metadata.modified()?, metadata.len(), entry.path()));
        }
    }
    stale.sort_by_key(|a| std::cmp::Reverse(a.0));
    let mut bytes = 0;
    for (index, (_, size, path)) in stale.into_iter().enumerate() {
        if index >= max_count || size > max_bytes - bytes {
            fs::remove_file(path)?;
        } else {
            bytes += size;
        }
    }
    Ok(())
}
fn watch_tree(path: &Path, output: &mut Vec<PathBuf>) {
    watch_directory(path, output, &mut HashSet::new());
}
fn watch_directory(path: &Path, output: &mut Vec<PathBuf>, visited: &mut HashSet<PathBuf>) {
    output.push(path.to_owned());
    if path.is_dir()
        && fs::canonicalize(path).is_ok_and(|canonical| visited.insert(canonical))
        && let Ok(entries) = fs::read_dir(path)
    {
        for entry in entries.flatten() {
            if entry.path().is_dir() {
                watch_directory(&entry.path(), output, visited);
            }
        }
    }
}
