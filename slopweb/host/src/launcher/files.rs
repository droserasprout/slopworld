//! Private filesystem transactions and content identities shared by preparation and AOT.
use anyhow::{Context, Result, ensure};
use fs2::FileExt;
use serde::{Deserialize, Serialize};
use serde_json::Value;
use sha2::{Digest, Sha256};
use std::{
    collections::BTreeMap,
    fs::{self, File, OpenOptions},
    io::{Read, Write},
    os::unix::fs::MetadataExt,
    path::{Path, PathBuf},
};

pub type Inputs = BTreeMap<String, PathBuf>;

/// The stager takes an exclusive lock on the parent before exchanging the bundle.
/// Hold one shared lock from the first helper read through the last source copy.
pub(super) struct PinnedBundle {
    path: PathBuf,
    _parent: File,
}

impl PinnedBundle {
    pub fn open(path: PathBuf) -> Result<Self> {
        let parent = File::open(path.parent().unwrap_or(&path))?;
        FileExt::lock_shared(&parent)?;
        Ok(Self {
            path,
            _parent: parent,
        })
    }

    pub fn path(&self) -> &Path {
        &self.path
    }
}

pub fn files(root: &Path) -> Result<Inputs> {
    fn visit(root: &Path, at: &Path, result: &mut Inputs) -> Result<()> {
        if !at.exists() {
            return Ok(());
        }
        for item in fs::read_dir(at)? {
            let item = item?;
            let path = item.path();
            ensure!(
                !item.file_type()?.is_symlink(),
                "Snapshots must not contain symlinks: {}",
                path.display()
            );
            if item.file_type()?.is_dir() {
                visit(root, &path, result)?;
            } else if item.file_type()?.is_file() {
                result.insert(
                    path.strip_prefix(root)?.to_string_lossy().into_owned(),
                    path,
                );
            }
        }
        Ok(())
    }
    ensure!(
        !root.is_symlink(),
        "Snapshot directory cannot be a symlink: {}",
        root.display()
    );
    let mut result = Inputs::new();
    visit(root, root, &mut result)?;
    Ok(result)
}

pub fn inputs(root: &Path, names: &[&str]) -> Result<Inputs> {
    let mut result = Inputs::new();
    for name in names {
        let path = root.join(name);
        ensure!(
            !path.is_symlink(),
            "Input cannot be a symlink: {}",
            path.display()
        );
        if path.is_dir() {
            for (relative, file) in files(&path)? {
                result.insert(format!("{name}/{relative}"), file);
            }
        } else if path.is_file() {
            result.insert((*name).into(), path);
        }
    }
    Ok(result)
}

pub fn digest_bytes(bytes: &[u8]) -> String {
    format!("{:x}", Sha256::digest(bytes))
}

pub fn file_digest(path: &Path) -> Result<String> {
    let mut file = File::open(path).with_context(|| format!("reading {}", path.display()))?;
    hash_reader(&mut file)
}

fn hash_reader(file: &mut File) -> Result<String> {
    let mut content = Sha256::new();
    let mut buffer = [0; 65536];
    loop {
        let size = file.read(&mut buffer)?;
        if size == 0 {
            break;
        }
        content.update(&buffer[..size]);
    }
    Ok(format!("{:x}", content.finalize()))
}

pub type Manifest = BTreeMap<String, String>;

#[derive(Clone, Copy, PartialEq, Eq, Serialize, Deserialize)]
struct Stamp {
    device: u64,
    inode: u64,
    size: u64,
    modified: (i64, i64),
    changed: (i64, i64),
}

impl From<fs::Metadata> for Stamp {
    fn from(metadata: fs::Metadata) -> Self {
        Self {
            device: metadata.dev(),
            inode: metadata.ino(),
            size: metadata.len(),
            modified: (metadata.mtime(), metadata.mtime_nsec()),
            changed: (metadata.ctime(), metadata.ctime_nsec()),
        }
    }
}

#[derive(Clone, PartialEq, Eq, Serialize, Deserialize)]
struct Entry {
    stamp: Stamp,
    digest: String,
}

/// The index is disposable. Persist just the last inventory to bound growth
/// across cache generations. Concurrent writers can only evict a memoized read.
pub struct DigestCache {
    path: PathBuf,
    previous: BTreeMap<PathBuf, Entry>,
    current: BTreeMap<PathBuf, Entry>,
}

impl DigestCache {
    pub fn load(path: PathBuf) -> Self {
        let previous = fs::read(&path)
            .ok()
            .and_then(|bytes| serde_json::from_slice(&bytes).ok())
            .unwrap_or_default();
        Self {
            path,
            previous,
            current: BTreeMap::new(),
        }
    }

    pub fn save(&self) -> Result<()> {
        if self.previous != self.current {
            // Linux also accepts non-UTF-8 paths. They participate in this
            // snapshot but cannot be keys in the persistent JSON index.
            let entries: BTreeMap<_, _> = self
                .current
                .iter()
                .filter_map(|(path, entry)| Some((path.to_str()?, entry)))
                .collect();
            atomic_write(&self.path, serde_json::to_vec(&entries)?)?;
        }
        Ok(())
    }

    fn entry(&self, path: &Path) -> Result<Entry> {
        let stamp = Stamp::from(fs::metadata(path)?);
        if let Some(entry) = self.current.get(path) {
            ensure!(
                entry.stamp == stamp,
                "Input {} changed during preparation; retry",
                path.display()
            );
            return Ok(entry.clone());
        }
        if let Some(entry) = self.previous.get(path)
            && entry.stamp == stamp
            && entry.digest.len() == 64
            && entry.digest.bytes().all(|b| b.is_ascii_hexdigit())
        {
            return Ok(entry.clone());
        }
        // A replacement between stat and open, or a writer during the read,
        // must never associate bytes from one revision with another's stamp.
        let mut file = File::open(path)?;
        let opened = Stamp::from(file.metadata()?);
        let digest = hash_reader(&mut file)?;
        ensure!(
            opened == Stamp::from(file.metadata()?) && opened == Stamp::from(fs::metadata(path)?),
            "Input {} changed during hashing; retry",
            path.display()
        );
        Ok(Entry {
            stamp: opened,
            digest,
        })
    }

    pub fn manifest(&mut self, inputs: &Inputs) -> Result<Manifest> {
        inputs
            .iter()
            .map(|(name, path)| {
                let entry = self.entry(path)?;
                let digest = entry.digest.clone();
                self.current.insert(path.clone(), entry);
                Ok((name.clone(), digest))
            })
            .collect()
    }

    pub fn copy_verified(&self, source: &Path, target: &Path) -> Result<()> {
        let stamp = self.current[source].stamp;
        let mut input = File::open(source)?;
        let opened = input.metadata()?;
        let permissions = opened.permissions();
        ensure!(
            stamp == Stamp::from(opened),
            "Input {} changed before copying; retry",
            source.display()
        );
        fs::create_dir_all(target.parent().unwrap())?;
        let mut output = File::create(target)?;
        std::io::copy(&mut input, &mut output)?;
        output.set_permissions(permissions)?;
        ensure!(
            stamp == Stamp::from(input.metadata()?) && stamp == Stamp::from(fs::metadata(source)?),
            "Input {} changed during copying; retry",
            source.display()
        );
        Ok(())
    }
}

pub fn digest(inputs: &Manifest, identity: &Value) -> Result<String> {
    let mut hash = Sha256::new();
    hash.update(serde_json::to_vec(&serde_json::json!([
        "content-manifest-v2",
        identity
    ]))?);
    for (name, content) in inputs {
        hash.update(serde_json::to_vec(&(name, content))?);
    }
    Ok(format!("{:x}", hash.finalize()))
}

pub fn copy_tree(source: &Path, target: &Path, skip: impl Fn(&str) -> bool) -> Result<()> {
    ensure!(
        source.is_dir(),
        "Missing input directory: {}",
        source.display()
    );
    fs::create_dir_all(target)?;
    for (name, source) in files(source)? {
        if skip(&name) {
            continue;
        }
        let target = target.join(name);
        fs::create_dir_all(target.parent().unwrap())?;
        fs::copy(source, target)?;
    }
    Ok(())
}

pub fn atomic_write(path: &Path, bytes: impl AsRef<[u8]>) -> Result<()> {
    let parent = path.parent().context("atomic file has no parent")?;
    fs::create_dir_all(parent)?;
    let mut temporary = tempfile::NamedTempFile::new_in(parent)?;
    temporary.write_all(bytes.as_ref())?;
    temporary.persist(path)?;
    Ok(())
}

pub fn lock(path: &Path) -> Result<File> {
    fs::create_dir_all(path.parent().unwrap())?;
    // The inode remains stable: compiler children can retain this flock after launcher exit.
    let file = OpenOptions::new().create(true).append(true).open(path)?;
    if let Err(error) = file.try_lock_exclusive() {
        if error.kind() != std::io::ErrorKind::WouldBlock {
            return Err(error.into());
        }
        eprintln!(
            "Waiting for build lock {}; a compiler may retain it after its launcher exits. Inspect with lslocks; do not delete the lock file.",
            path.display()
        );
        file.lock_exclusive()?;
        eprintln!("Build lock acquired; continuing: {}", path.display());
    }
    Ok(file)
}

/// A forced rebuild can publish another generation of the same input key.
pub fn ready(root: &Path, key: &str) -> Result<bool> {
    let marker = root.join(".ready");
    ensure!(
        !root.is_symlink() && !marker.is_symlink(),
        "Runtime cache cannot use a symlink: {}",
        root.display()
    );
    match fs::read(&marker) {
        Ok(bytes) => Ok(bytes.strip_suffix(b"\n").unwrap_or(&bytes) == key.as_bytes()),
        Err(error) if error.kind() == std::io::ErrorKind::NotFound => Ok(false),
        Err(error) => Err(error.into()),
    }
}

pub fn selected(parent: &Path, key: &str) -> Result<PathBuf> {
    let pointer = parent.join(format!(".active-{key}"));
    if !pointer.is_file() {
        return Ok(parent.join(key));
    }
    let selected = fs::read_to_string(pointer)?;
    let name = selected.trim();
    ensure!(
        Path::new(name).file_name().and_then(|x| x.to_str()) == Some(name)
            && (name == key
                || name
                    .strip_prefix(key)
                    .is_some_and(|suffix| suffix.starts_with('-'))),
        "Invalid private runtime cache pointer"
    );
    let root = parent.join(name);
    ensure!(
        !root.is_symlink(),
        "Runtime cache directory cannot be a symlink"
    );
    Ok(root)
}

pub fn unique_name(key: &str) -> String {
    use std::sync::atomic::{AtomicU64, Ordering};
    static NEXT: AtomicU64 = AtomicU64::new(0);
    format!(
        "{key}-{}-{}-{}",
        std::process::id(),
        std::time::SystemTime::now()
            .duration_since(std::time::UNIX_EPOCH)
            .unwrap_or_default()
            .as_nanos(),
        NEXT.fetch_add(1, Ordering::Relaxed)
    )
}
