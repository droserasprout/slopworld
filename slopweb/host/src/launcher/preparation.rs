//! Immutable managed, archive, font and background phases under one transaction lock.
use super::{
    appearance,
    files::{self, Inputs},
};
use anyhow::{Result, ensure};
use serde_json::{Map, Value, json};
use std::{
    collections::BTreeMap,
    fs::{self, File},
    io,
    path::{Path, PathBuf},
    process::Command,
    time::Instant,
};
use zip::{ZipWriter, write::SimpleFileOptions};

fn top_level(root: &Path, predicate: impl Fn(&str) -> bool) -> Result<Inputs> {
    let mut result = Inputs::new();
    if root.is_dir() {
        for item in fs::read_dir(root)? {
            let item = item?;
            let name = item.file_name().to_string_lossy().into_owned();
            if predicate(&name) && item.path().is_file() {
                result.insert(name, item.path());
            }
        }
    }
    Ok(result)
}
fn prefixed(target: &mut Inputs, prefix: &str, source: Inputs) {
    target.extend(
        source
            .into_iter()
            .map(|(name, path)| (format!("{prefix}/{name}"), path)),
    );
}

pub fn core_archive(game: &Path, destination: &Path) -> Result<()> {
    let inputs = files::inputs(game, &["Data/Core", "Rendering/Shaders"])?;
    let mut archive = ZipWriter::new(File::create(destination)?);
    let options = SimpleFileOptions::default()
        .compression_method(zip::CompressionMethod::Deflated)
        .unix_permissions(0o644);
    for (name, path) in inputs {
        ensure!(
            !path.is_symlink()
                && !path
                    .extension()
                    .is_some_and(|x| x.eq_ignore_ascii_case("dll")),
            "Core-only staging rejects {}",
            path.display()
        );
        archive.start_file(name, options)?;
        io::copy(&mut File::open(path)?, &mut archive)?;
    }
    archive.finish()?;
    Ok(())
}

pub fn native_phase(bundle: &Path, game: &Path, phase: &str, root: &Path) -> Result<()> {
    let run = |args: &[&Path]| -> Result<()> {
        let status = Command::new("dotnet").args(args).status()?;
        ensure!(
            status.success(),
            "Native preparation helper failed ({status}): {}",
            args[0].display()
        );
        Ok(())
    };
    match phase {
        "managed" => {
            fs::copy(bundle.join("Adapters.dll"), root.join("Adapters.dll"))?;
            run(&[
                &bundle.join("scanner/Scanner.dll"),
                game,
                Path::new("--stage"),
                &root.join("game"),
                Path::new("--jobs"),
            ])?;
            run(&[
                &bundle.join("offline/Offline.dll"),
                Path::new("--core"),
                root,
                &bundle.join("Probe.dll"),
            ])?;
            fs::create_dir_all(root.join("mod"))?;
            for (name, path) in top_level(&bundle.join("mod"), |name| name.ends_with(".dll"))? {
                fs::copy(path, root.join("mod").join(name))?;
            }
            run(&[
                &bundle.join("offline/Offline.dll"),
                Path::new("--eco"),
                root,
                &bundle.join("mod/SlopWorld.dll"),
            ])?;
        }
        "assets" => {
            fs::create_dir_all(root.join("assets"))?;
            fs::copy(game.join("Assets.zip"), root.join("assets/Assets.zip"))?;
            core_archive(game, &root.join("assets/Core.zip"))?;
        }
        "background" => run(&[
            &bundle.join("background-probe/BackgroundProbe.dll"),
            game,
            &bundle.join("mod/SlopWorld.dll"),
            &root.join("assets/background"),
        ])?,
        _ => anyhow::bail!("Unknown preparation phase {phase}"),
    }
    Ok(())
}

pub fn prepare(
    bundle: &Path,
    game: &Path,
    cache: &Path,
    rebuild: bool,
    appearance: &Map<String, Value>,
    font_inventory: &str,
    build: impl Fn(&str, &Path, &Map<String, Value>) -> Result<()>,
) -> Result<PathBuf> {
    let game_dlls = top_level(game, |name| name.ends_with(".dll"))?;
    let native = top_level(game, |name| name.contains(".so"))?;
    let mut managed = files::inputs(bundle, &["scanner", "offline", "Probe.dll", "Adapters.dll"])?;
    prefixed(
        &mut managed,
        "mod",
        top_level(&bundle.join("mod"), |name| name.ends_with(".dll"))?,
    );
    prefixed(&mut managed, "game", game_dlls.clone());
    let assets_inputs = files::inputs(game, &["Assets.zip", "Data/Core", "Rendering/Shaders"])?;
    let mut fonts = files::inputs(bundle, &["mod/SlopWorld.dll"])?;
    let mut background = files::inputs(bundle, &["background-probe", "mod/SlopWorld.dll"])?;
    for inputs in [&mut fonts, &mut background] {
        prefixed(inputs, "game", game_dlls.clone());
        prefixed(inputs, "game", native.clone());
    }
    background.extend(files::inputs(game, &["Assets.zip"])?);
    let font_settings: Map<_, _> = appearance
        .iter()
        .filter(|(key, _)| {
            ["fontName", "fontSize", "uiFontName", "uiFontSize"].contains(&key.as_str())
        })
        .map(|(k, v)| (k.clone(), v.clone()))
        .collect();
    let mut contents = BTreeMap::new();
    let mut keys = BTreeMap::new();
    for (name, inputs, identity) in [
        ("managed", managed, json!("managed-rust-v1")),
        ("assets", assets_inputs, json!("assets-rust-v1")),
        (
            "fonts",
            fonts,
            json!(["fonts-rust-v1", game, font_settings, font_inventory]),
        ),
        ("background", background, json!(["background-v1", game])),
    ] {
        keys.insert(name, files::digest(&inputs, &identity, &mut contents)?);
    }
    let mut authored = files::inputs(bundle, &["web", "ost"])?;
    prefixed(
        &mut authored,
        "mod",
        files::files(&bundle.join("mod"))?
            .into_iter()
            .filter(|(name, _)| !name.ends_with(".dll"))
            .collect(),
    );
    let key = files::digest(&authored, &json!([keys, appearance]), &mut contents)?[..24].to_owned();
    fs::create_dir_all(cache)?;
    let _transaction = files::lock(&cache.join(".prepare.lock"))?;
    let mut destination = files::selected(cache, &key)?;
    if destination.join(".ready").is_file() && !rebuild {
        println!("Reusing prepared runtime");
        return Ok(destination);
    }
    let mut components = Vec::new();
    let mut pointers = Vec::new();
    for phase in ["managed", "assets", "fonts", "background"] {
        let parent = cache.join("prepared-components").join(phase);
        fs::create_dir_all(&parent)?;
        let phase_key = &keys[phase];
        let mut component = files::selected(&parent, phase_key)?;
        if rebuild || (component.exists() && !component.join(".ready").is_file()) {
            component = parent.join(files::unique_name(phase_key));
        }
        if !component.join(".ready").is_file() {
            let temporary = tempfile::Builder::new()
                .prefix(".prepare-")
                .tempdir_in(&parent)?;
            let root = temporary.path().join("runtime");
            fs::create_dir(&root)?;
            let started = Instant::now();
            build(phase, &root, &font_settings)?;
            println!("Prepare {phase}: {:.2}s", started.elapsed().as_secs_f64());
            fs::write(root.join(".ready"), phase_key)?;
            fs::rename(root, &component)?;
        } else {
            println!("Reusing prepared {phase}");
        }
        pointers.push((
            parent.join(format!(".active-{phase_key}")),
            component
                .file_name()
                .unwrap()
                .to_string_lossy()
                .into_owned(),
        ));
        components.push(component);
    }
    if destination.exists() {
        destination = cache.join(files::unique_name(&key));
    }
    let temporary = tempfile::Builder::new()
        .prefix(".prepare-")
        .tempdir_in(cache)?;
    let root = temporary.path().join("runtime");
    files::copy_tree(&bundle.join("web"), &root, |_| false)?;
    for component in &components {
        files::copy_tree(component, &root, |name| name == ".ready")?;
    }
    for (name, source) in &authored {
        if name.starts_with("mod/") {
            let target = root.join(name);
            fs::create_dir_all(target.parent().unwrap())?;
            fs::copy(source, target)?;
        }
    }
    files::copy_tree(&bundle.join("ost"), &root.join("assets/ost"), |_| false)?;
    let mut values = appearance.clone();
    let resolved = components[2].join("assets/reference-appearance.json");
    if resolved.is_file() {
        values.extend(serde_json::from_slice::<Map<String, Value>>(&fs::read(
            resolved,
        )?)?);
    }
    appearance::write_preferences(&root, &values)?;
    fs::write(root.join(".ready"), format!("{key}\n"))?;
    fs::rename(root, &destination)?;
    // Pointers change only after every phase and composition succeeded. Older
    // served roots remain untouched even when a forced rebuild partially fails.
    for (pointer, selected) in pointers {
        files::atomic_write(&pointer, selected)?;
    }
    files::atomic_write(
        &cache.join(format!(".active-{key}")),
        destination.file_name().unwrap().as_encoded_bytes(),
    )?;
    Ok(destination)
}
