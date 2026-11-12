//! Installed font discovery, persistent coverage and file delivery. The launcher
//! owns preference import and the server owns HTTP authorization and response limits.
mod cache;
mod flags;
mod request;
mod startup;
use flags::loader_flags;
use request::{Request, parse_request, validate_family};
mod fontconfig;
mod opentype;
#[cfg(test)]
mod tests;

use anyhow::{Context, Result, ensure};
use base64::{Engine, engine::general_purpose::STANDARD};
use cache::Catalog;
use fs2::FileExt;
use opentype::Face;
use serde_json::{Value, json};
use std::{
    collections::{BTreeMap, HashSet},
    fs::{self, File},
    path::{Path, PathBuf},
    sync::{Arc, Mutex},
    time::{Duration, Instant},
};

const UI_CANDIDATES: &[&str] = &[
    "PT Sans",
    "Noto Sans",
    "Inter",
    "Inter Variable",
    "Adwaita Sans",
    "Cantarell",
    "DejaVu Sans",
    "Liberation Sans",
    "Arial",
    "Segoe UI",
    "Helvetica",
    "sans-serif",
];
#[derive(Clone)]
pub struct FontService {
    inner: Arc<Inner>,
}
struct Inner {
    root: PathBuf,
    flags: u32,
    state: Mutex<State>,
}
struct State {
    catalog: Catalog,
    checked: Option<Instant>,
}
struct DiskLock(File);
impl Drop for DiskLock {
    fn drop(&mut self) {
        let _ = FileExt::unlock(&self.0);
    }
}
fn lock(root: &Path) -> Result<DiskLock> {
    let file = File::options()
        .create(true)
        .truncate(false)
        .read(true)
        .write(true)
        .open(root.join(".lock"))?;
    FileExt::lock_exclusive(&file)?;
    Ok(DiskLock(file))
}

impl FontService {
    pub fn new(bundle: &Path, game: &Path, cache: &Path) -> Result<Self> {
        let root = cache.join("font-index-v1");
        fs::create_dir_all(&root)?;
        let _disk = lock(&root)?;
        let flags = loader_flags(bundle, game, &root)?;
        let catalog = Catalog::load(&root);
        Ok(Self {
            inner: Arc::new(Inner {
                root,
                flags,
                state: Mutex::new(State {
                    catalog,
                    checked: None,
                }),
            }),
        })
    }
    /// The async boundary keeps font/config IO off the HTTP executor. The owned
    /// operation remains serialized through publication even if its caller cancels.
    pub async fn request(&self, value: Value) -> Result<Value> {
        let this = self.clone();
        tokio::task::spawn_blocking(move || this.request_sync(value)).await?
    }
    pub async fn catalog(&self) -> Result<Value> {
        self.catalog_for("monospace").await
    }
    pub fn startup_fingerprint(&self) -> Result<String> {
        self.with_catalog(|catalog| {
            Ok(cache::digest(&serde_json::to_vec(&(
                1,
                &catalog.generation,
                self.inner.flags,
            ))?))
        })
    }
    pub async fn catalog_for(&self, family: &str) -> Result<Value> {
        validate_family(family)?;
        let this = self.clone();
        let family = family.to_owned();
        tokio::task::spawn_blocking(move || {
            this.with_catalog(|catalog| this.catalog_value(catalog, &family))
        })
        .await?
    }
    pub async fn font_bytes(&self, hash: &str) -> Result<Vec<u8>> {
        valid_hash(hash)?;
        let service = self.clone();
        let hash = hash.to_ascii_lowercase();
        tokio::task::spawn_blocking(move || {
            if let Ok(bytes) = read_object(&service.inner.root, &hash) {
                return Ok(bytes);
            }
            service.with_catalog(|catalog| {
                catalog.restore_object(&service.inner.root, &hash)?;
                read_object(&service.inner.root, &hash)
            })
        })
        .await?
    }
    fn with_catalog<T>(&self, operation: impl FnOnce(&mut Catalog) -> Result<T>) -> Result<T> {
        let mut state = self
            .inner
            .state
            .lock()
            .map_err(|_| anyhow::anyhow!("Font service lock poisoned"))?;
        let _disk = lock(&self.inner.root)?;
        if state
            .checked
            .is_none_or(|t| t.elapsed() >= Duration::from_secs(2))
        {
            if !state.catalog.fresh(&self.inner.root) {
                let disk = Catalog::load(&self.inner.root);
                if disk.fresh(&self.inner.root) {
                    state.catalog = disk;
                } else {
                    state.catalog.rebuild(&self.inner.root)?;
                }
            }
            state.checked = Some(Instant::now());
        }
        operation(&mut state.catalog)
    }
    fn catalog_value(&self, catalog: &mut Catalog, family: &str) -> Result<Value> {
        let chain = catalog.chain(&self.inner.root, family)?;
        let order: Vec<_> = chain
            .iter()
            .filter_map(|name| find(catalog, name).map(|f| f.hash.clone()))
            .collect();
        let preload = preload(catalog, &order);
        let faces:Vec<_>=catalog.faces.iter().map(|f|json!({"hash":f.hash,"family":f.family,"coverage":f.coverage,"size":14,"loaderFlags":self.inner.flags})).collect();
        let all: Vec<_> = catalog.faces.iter().map(|f| &f.family).collect();
        let mono: Vec<_> = catalog
            .faces
            .iter()
            .filter(|f| f.mono)
            .map(|f| &f.family)
            .collect();
        Ok(
            json!({"version":1,"revision":catalog.generation,"generation":catalog.generation,"faces":faces,"order":order,"preload":preload,"all":all,"mono":mono}),
        )
    }
    fn request_sync(&self, value: Value) -> Result<Value> {
        let started = Instant::now();
        let request = parse_request(&value)?;
        let result = self.with_catalog(|catalog| {
            let mut delivery = Delivery::new(
                catalog.generation.clone(),
                value.get("protocol").and_then(Value::as_u64) == Some(2),
            );
            match request {
                Request::Selection(ref selection) => {
                    let ui = select_ui(catalog, &selection.ui)?;
                    let chain = catalog.chain(&self.inner.root, &selection.terminal)?;
                    let terminal = chain
                        .first()
                        .and_then(|n| find(catalog, n))
                        .context("No terminal font available")?
                        .clone();
                    for offset in [-2, 0, 2] {
                        delivery.add(self, &ui, "ui.ttf", (selection.ui_size + offset).max(1))?;
                    }
                    delivery.add(self, &terminal, "terminal.ttf", selection.terminal_size)?;
                    delivery
                        .chains
                        .insert(selection.terminal.clone(), json!(chain));
                }
                Request::Fallback { family, scalars } => {
                    let chain = catalog.chain(&self.inner.root, &family)?;
                    let mut pending: HashSet<_> = scalars.iter().copied().collect();
                    delivery.assignments = Some(
                        scalars
                            .iter()
                            .map(|s| (s.to_string(), Value::Null))
                            .collect(),
                    );
                    let mut count = 0;
                    for name in &chain {
                        if pending.is_empty() {
                            break;
                        }
                        let Some(face) = find(catalog, name) else {
                            continue;
                        };
                        let covered: Vec<_> = pending
                            .iter()
                            .copied()
                            .filter(|s| opentype::contains(&face.coverage, *s))
                            .collect();
                        if covered.is_empty() {
                            continue;
                        }
                        let filename = format!("face{count}.ttf");
                        count += 1;
                        delivery.add(self, face, &filename, 14)?;
                        for scalar in covered {
                            delivery
                                .assignments
                                .as_mut()
                                .unwrap()
                                .insert(scalar.to_string(), json!(filename));
                            pending.remove(&scalar);
                        }
                    }
                    delivery.chains.insert(family, json!(chain));
                }
            }
            Ok(delivery.value())
        });
        eprintln!(
            "slopweb fonts: request completed in {:.3}s",
            started.elapsed().as_secs_f64()
        );
        result
    }
}

fn find<'a>(catalog: &'a Catalog, name: &str) -> Option<&'a Face> {
    catalog
        .faces
        .iter()
        .find(|f| f.family.to_lowercase() == name.to_lowercase())
}
fn select_ui(catalog: &Catalog, name: &str) -> Result<Face> {
    if !name.is_empty() {
        return find(catalog, name)
            .cloned()
            .context("Selected UI font is unavailable");
    }
    UI_CANDIDATES
        .iter()
        .find_map(|name| find(catalog, name))
        .cloned()
        .context("No default UI font is installed")
}
fn valid_hash(hash: &str) -> Result<()> {
    ensure!(
        hash.len() == 64 && hash.bytes().all(|b| b.is_ascii_hexdigit()),
        "Invalid font hash"
    );
    Ok(())
}
fn read_object(root: &Path, hash: &str) -> Result<Vec<u8>> {
    valid_hash(hash)?;
    let bytes = fs::read(cache::object(root, hash))?;
    ensure!(cache::digest(&bytes) == hash, "Cached font hash mismatch");
    Ok(bytes)
}
fn preload(catalog: &Catalog, order: &[String]) -> Vec<String> {
    let mut seen = vec![false; 0x110000];
    let mut result = Vec::new();
    let mut remaining = 2 * 1024 * 1024;
    for hash in order {
        let Some(face) = catalog.faces.iter().find(|f| &f.hash == hash) else {
            continue;
        };
        if face.byte_len > remaining {
            continue;
        }
        let mut contributes = false;
        for &[start, end] in &face.coverage {
            for c in start..=end {
                if !seen[c as usize] {
                    contributes = true;
                    seen[c as usize] = true;
                }
            }
        }
        if contributes {
            result.push(hash.clone());
            remaining -= face.byte_len;
            if result.len() == 3 {
                break;
            }
        }
    }
    result
}
struct Delivery {
    fonts: BTreeMap<String, String>,
    files: BTreeMap<String, String>,
    faces: Vec<Value>,
    chains: BTreeMap<String, Value>,
    assignments: Option<BTreeMap<String, Value>>,
    generation: String,
    modern: bool,
}
impl Delivery {
    fn new(generation: String, modern: bool) -> Self {
        Self {
            fonts: BTreeMap::new(),
            files: BTreeMap::new(),
            faces: Vec::new(),
            chains: BTreeMap::new(),
            assignments: None,
            generation,
            modern,
        }
    }
    fn add(&mut self, service: &FontService, face: &Face, filename: &str, size: i64) -> Result<()> {
        if !self.files.contains_key(filename) {
            self.files.insert(filename.into(), face.hash.clone());
            if !self.modern {
                self.fonts.insert(
                    filename.into(),
                    STANDARD.encode(read_object(&service.inner.root, &face.hash)?),
                );
            }
        }
        let bake =
            json!({"size":size,"loaderFlags":service.inner.flags,"family":face.family}).to_string();
        if !self
            .faces
            .iter()
            .any(|f| f["font"] == filename && f["bake"] == bake)
        {
            self.faces.push(json!({"font":filename,"bake":bake}));
        }
        Ok(())
    }
    fn value(self) -> Value {
        let mut value = json!({"fonts":self.fonts,"files":self.files,"faces":self.faces,"chains":self.chains,"generation":self.generation});
        if let Some(a) = self.assignments {
            value["assignments"] = json!(a);
        }
        value
    }
}
