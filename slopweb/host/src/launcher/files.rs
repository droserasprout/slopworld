//! Private filesystem transactions and content identities shared by preparation and AOT.
use anyhow::{Context, Result, ensure};
use fs2::FileExt;
use serde_json::Value;
use sha2::{Digest, Sha256};
use std::{
    collections::BTreeMap,
    fs::{self, File, OpenOptions},
    io::{Read, Write},
    path::{Path, PathBuf},
};

pub type Inputs = BTreeMap<String, PathBuf>;

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

pub fn digest(
    inputs: &Inputs,
    identity: &Value,
    memo: &mut BTreeMap<PathBuf, String>,
) -> Result<String> {
    let mut hash = Sha256::new();
    hash.update(serde_json::to_vec(&serde_json::json!([
        "content-manifest-v2",
        identity
    ]))?);
    for (name, path) in inputs {
        let content = if let Some(content) = memo.get(path) {
            content.clone()
        } else {
            let mut file =
                File::open(path).with_context(|| format!("reading {}", path.display()))?;
            let mut content = Sha256::new();
            let mut buffer = [0; 65536];
            loop {
                let size = file.read(&mut buffer)?;
                if size == 0 {
                    break;
                }
                content.update(&buffer[..size]);
            }
            let content = format!("{:x}", content.finalize());
            memo.insert(path.clone(), content.clone());
            content
        };
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

pub fn selected(parent: &Path, key: &str) -> Result<PathBuf> {
    let pointer = parent.join(format!(".active-{key}"));
    if !pointer.is_file() {
        return Ok(parent.join(key));
    }
    let selected = fs::read_to_string(pointer)?;
    let name = selected.trim();
    ensure!(
        Path::new(name).file_name().and_then(|x| x.to_str()) == Some(name) && name.starts_with(key),
        "Invalid private runtime cache pointer"
    );
    Ok(parent.join(name))
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
