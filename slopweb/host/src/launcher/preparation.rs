//! Immutable managed, archive, font and background phases under one transaction lock.
use super::{
    appearance,
    files::{self, Inputs},
};
use crate::archive_identity;
use anyhow::{Context, Result, ensure};
use serde_json::{Map, Value, json};
use std::{
    collections::BTreeMap,
    fs::{self, File},
    path::{Path, PathBuf},
    process::Command,
    time::Instant,
};
use zip::ZipWriter;

mod core;
pub(super) use core::core_archive;

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

// Entry resolves Core defs and texture aliases synchronously, including assets outside UI/.
// Keep their baked catalogs and texture sources together. Eco uses the Core background
// and streams its own soundtrack through workspace-audio.js.
fn eco_member(name: &str) -> bool {
    if name.starts_with("Resources/Sounds/") {
        return name.starts_with("Resources/Sounds/UI/")
            || name == "Resources/Sounds/PlaceholderWav.ogg";
    }
    !name.starts_with("Resources/Textures/UI/HeroArt/MenuBG_")
}

fn eco_archive(source: &Path, destination: &Path) -> Result<()> {
    let mut source = zip::ZipArchive::new(File::open(source)?)?;
    let mut archive = ZipWriter::new(File::create(destination)?);
    for index in 0..source.len() {
        // Waylith reads stored and Zstandard members directly from the ZIP mapping.
        let member = source.by_index_raw(index)?;
        if eco_member(member.name()) {
            archive.raw_copy_file(member)?;
        }
    }
    archive.finish()?;
    Ok(())
}

// The immutable assets component outlives individual compositions. Keep its
// digest inventory separate from the game and bundle inputs checked on every launch.
fn copy_eco_archive(component: &Path, root: &Path, cache: &Path) -> Result<()> {
    let inputs = files::inputs(component, &[archive_identity::ARCHIVE])?;
    let Some(source) = inputs.get(archive_identity::ARCHIVE) else {
        return Ok(());
    };
    let mut contents = files::DigestCache::load(cache.join("eco-digest-v1.json"));
    let hashes = contents.manifest(&inputs)?;
    contents.copy_verified(source, &root.join(archive_identity::ARCHIVE))?;
    archive_identity::publish(
        root,
        hashes
            .get(archive_identity::ARCHIVE)
            .context("Missing archive digest")?,
    )?;
    contents.save()
}

#[derive(Clone, Copy, Debug, Eq, PartialEq, Ord, PartialOrd, serde::Serialize)]
#[serde(rename_all = "lowercase")]
pub enum Phase {
    Managed,
    Assets,
    Fonts,
    Background,
}

impl Phase {
    pub(super) fn build(
        self,
        inputs: &Path,
        root: &Path,
        fonts: impl FnOnce() -> Result<()>,
    ) -> Result<()> {
        let bundle = inputs.join("bundle");
        let game = inputs.join("game");
        match self {
            Self::Managed => managed(&bundle, &game, root),
            Self::Assets => assets(&game, root),
            Self::Fonts => fonts(),
            Self::Background => background(&bundle, &game, root),
        }
    }

    fn directory(self) -> &'static str {
        match self {
            Self::Managed => "managed",
            Self::Assets => "assets",
            Self::Fonts => "fonts",
            Self::Background => "background",
        }
    }
}

fn run_helper(args: &[&Path]) -> Result<()> {
    let status = Command::new("dotnet").args(args).status()?;
    ensure!(
        status.success(),
        "Native preparation helper failed ({status}): {}",
        args.first().context("Missing helper")?.display()
    );
    Ok(())
}

fn managed(bundle: &Path, game: &Path, root: &Path) -> Result<()> {
    fs::copy(bundle.join("Adapters.dll"), root.join("Adapters.dll"))?;
    run_helper(&[
        &bundle.join("scanner/Scanner.dll"),
        game,
        Path::new("--stage"),
        &root.join("game"),
        Path::new("--jobs"),
    ])?;
    run_helper(&[
        &bundle.join("offline/Offline.dll"),
        Path::new("--core"),
        root,
        &bundle.join("Probe.dll"),
    ])?;
    fs::create_dir_all(root.join("mod"))?;
    for (name, path) in top_level(&bundle.join("mod"), |name| name.ends_with(".dll"))? {
        fs::copy(path, root.join("mod").join(name))?;
    }
    run_helper(&[
        &bundle.join("offline/Offline.dll"),
        Path::new("--eco"),
        root,
        &bundle.join("mod/SlopWorld.dll"),
    ])
}

pub(super) fn assets(game: &Path, root: &Path) -> Result<()> {
    fs::create_dir_all(root.join("assets"))?;
    eco_archive(&game.join("Assets.zip"), &root.join("assets/Eco.zip"))?;
    core_archive(game, &root.join("assets/Core.zip"))
}

fn background(bundle: &Path, game: &Path, root: &Path) -> Result<()> {
    run_helper(&[
        &bundle.join("background-probe/BackgroundProbe.dll"),
        game,
        &bundle.join("mod/SlopWorld.dll"),
        &root.join("assets/background"),
    ])
}

struct PlannedInputs {
    paths: Inputs,
    digests: files::Manifest,
}

impl PlannedInputs {
    fn new(paths: Inputs, contents: &mut files::DigestCache) -> Result<Self> {
        let digests = contents.manifest(&paths)?;
        Ok(Self { paths, digests })
    }

    fn copy_tree(&self, prefix: &str, target: &Path) -> Result<()> {
        fs::create_dir_all(target)?;
        for (name, source) in &self.paths {
            if let Ok(relative) = Path::new(name).strip_prefix(prefix) {
                let output = target.join(relative);
                fs::create_dir_all(output.parent().context("Missing output directory")?)?;
                fs::copy(source, &output)?;
                ensure!(
                    Some(&files::file_digest(&output)?) == self.digests.get(name),
                    "Input {name} changed during preparation; retry"
                );
            }
        }
        Ok(())
    }
}

struct ComponentPlan {
    inputs: PlannedInputs,
    key: String,
}

struct PreparationPlan {
    authored: PlannedInputs,
    key: String,
    components: BTreeMap<Phase, ComponentPlan>,
    font_settings: Map<String, Value>,
}

fn plan(
    bundle: &Path,
    game: &Path,
    cache: &Path,
    appearance: &Map<String, Value>,
    font_inventory: &str,
) -> Result<PreparationPlan> {
    let game_dlls = top_level(game, |name| name.ends_with(".dll"))?;
    let native = top_level(game, |name| name.contains(".so"))?;
    let mod_dlls = top_level(&bundle.join("mod"), |name| name.ends_with(".dll"))?;
    let mut managed = files::inputs(bundle, &["scanner", "offline", "Probe.dll", "Adapters.dll"])?;
    prefixed(&mut managed, "mod", mod_dlls.clone());
    let mut background = files::inputs(bundle, &["background-probe"])?;
    prefixed(&mut background, "mod", mod_dlls);
    let mut native_game = game_dlls.clone();
    native_game.extend(native);
    let mut background_game = native_game.clone();
    background_game.extend(files::inputs(game, &["Assets.zip"])?);
    let font_settings: Map<_, _> = appearance
        .iter()
        .filter(|(key, _)| {
            ["fontName", "fontSize", "uiFontName", "uiFontSize"].contains(&key.as_str())
        })
        .map(|(k, v)| (k.clone(), v.clone()))
        .collect();
    let mut contents = files::DigestCache::load(cache.join("preparation-digests-v1.json"));
    let mut components = BTreeMap::new();
    for (phase, bundle_files, game_files, policy) in [
        (Phase::Managed, managed, game_dlls, json!("managed-rust-v1")),
        (
            Phase::Assets,
            Inputs::new(),
            files::inputs(game, &["Assets.zip", "Data/Core", "Rendering/Shaders"])?,
            json!("eco-assets-locales-v2"),
        ),
        (
            Phase::Fonts,
            files::inputs(bundle, &["mod/SlopWorld.dll"])?,
            native_game,
            json!(["font-manifest-v2", game, font_settings, font_inventory]),
        ),
        (
            Phase::Background,
            background,
            background_game,
            json!(["background-v1", game]),
        ),
    ] {
        let mut inputs = Inputs::new();
        prefixed(&mut inputs, "bundle", bundle_files);
        prefixed(&mut inputs, "game", game_files);
        let inputs = PlannedInputs::new(inputs, &mut contents)?;
        let key = files::digest(&inputs.digests, &json!(["captured-phase-v1", policy]))?;
        components.insert(phase, ComponentPlan { inputs, key });
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
    let authored = PlannedInputs::new(authored, &mut contents)?;
    let keys: BTreeMap<_, _> = components
        .iter()
        .map(|(phase, plan)| (phase, &plan.key))
        .collect();
    let key = files::digest(
        &authored.digests,
        &json!(["archive-identity-v1", keys, appearance]),
    )?
    .get(..24)
    .context("Invalid preparation digest")?
    .to_owned();
    contents.save()?;
    Ok(PreparationPlan {
        authored,
        key,
        components,
        font_settings,
    })
}

impl PreparationPlan {
    fn output(&self, cache: &Path, root: PathBuf) -> Result<Candidate> {
        let components = self
            .components
            .iter()
            .map(|(phase, planned)| {
                files::selected(
                    &cache.join("prepared-components").join(phase.directory()),
                    &planned.key,
                )
            })
            .collect::<Result<_>>()?;
        Ok(Candidate { root, components })
    }

    fn copy_authored(&self, bundle: &Path, directory: &str, target: &Path) -> Result<()> {
        // A package manager or local editor can replace a file without taking the
        // stager's lock. Verify the private copy, and copy only the hashed manifest.
        ensure!(
            bundle.join(directory).is_dir(),
            "Missing bundle directory {directory}"
        );
        self.authored.copy_tree(directory, target)
    }
}

#[cfg(test)]
pub fn prepare(
    bundle: &files::PinnedBundle,
    game: &Path,
    cache: &Path,
    rebuild: bool,
    appearance: &Map<String, Value>,
    font_inventory: &str,
    build: impl Fn(Phase, &Path, &Path, &Map<String, Value>) -> Result<()>,
) -> Result<PathBuf> {
    prepare_candidate(
        bundle,
        game,
        cache,
        rebuild,
        appearance,
        font_inventory,
        build,
    )
    .map(|output| output.root)
}

pub(super) struct Candidate {
    pub root: PathBuf,
    pub components: Vec<PathBuf>,
}

pub(super) fn prepare_candidate(
    bundle: &files::PinnedBundle,
    game: &Path,
    cache: &Path,
    rebuild: bool,
    appearance: &Map<String, Value>,
    font_inventory: &str,
    build: impl Fn(Phase, &Path, &Path, &Map<String, Value>) -> Result<()>,
) -> Result<Candidate> {
    let _transaction = files::lock(&cache.join(".prepare.lock"))?;
    let bundle = bundle.path();
    let plan = plan(bundle, game, cache, appearance, font_inventory)?;
    let key = &plan.key;
    let mut destination = files::selected(cache, key)?;
    if !rebuild && files::ready(&destination, key)? {
        println!("Reusing prepared runtime");
        return plan.output(cache, destination);
    }
    let mut pointers = Vec::new();
    let mut prepare_component = |phase: Phase| -> Result<PathBuf> {
        let name = phase.directory();
        let parent = cache.join("prepared-components").join(name);
        fs::create_dir_all(&parent)?;
        let planned = plan.components.get(&phase).context("Missing phase plan")?;
        let phase_key = &planned.key;
        let mut component = files::selected(&parent, phase_key)?;
        if rebuild || !files::ready(&component, phase_key)? {
            if component.exists() || rebuild {
                component = parent.join(files::unique_name(phase_key));
            }
            let temporary = tempfile::Builder::new()
                .prefix(".prepare-")
                .tempdir_in(&parent)?;
            let root = temporary.path().join("runtime");
            fs::create_dir(&root)?;
            let started = Instant::now();
            // Helpers enumerate and reopen paths. Give them only the inventory and
            // bytes that produced this key, even if the live install changes back
            // before publication. Startup fonts use the in-memory font service.
            let captured = temporary.path().join("inputs");
            if phase != Phase::Fonts {
                planned.inputs.copy_tree("", &captured)?;
            }
            build(phase, &captured, &root, &plan.font_settings)?;
            println!("Prepare {name}: {:.2}s", started.elapsed().as_secs_f64());
            fs::write(root.join(".ready"), phase_key)?;
            fs::rename(root, &component)?;
        } else {
            println!("Reusing prepared {name}");
        }
        pointers.push((
            parent.join(format!(".active-{phase_key}")),
            component
                .file_name()
                .context("Missing component filename")?
                .to_string_lossy()
                .into_owned(),
        ));
        Ok(component)
    };
    let managed = prepare_component(Phase::Managed)?;
    let assets = prepare_component(Phase::Assets)?;
    let fonts = prepare_component(Phase::Fonts)?;
    let background = prepare_component(Phase::Background)?;
    if destination.exists() {
        destination = cache.join(files::unique_name(key));
    }
    let temporary = tempfile::Builder::new()
        .prefix(".prepare-")
        .tempdir_in(cache)?;
    let root = temporary.path().join("runtime");
    plan.copy_authored(bundle, "web", &root)?;
    for component in [&managed, &assets, &fonts, &background] {
        files::copy_tree(component, &root, |name| {
            name == ".ready" || name == archive_identity::ARCHIVE
        })?;
    }
    copy_eco_archive(&assets, &root, cache)?;
    if plan
        .authored
        .paths
        .keys()
        .any(|name| name.starts_with("mod/"))
    {
        plan.copy_authored(bundle, "mod", &root.join("mod"))?;
    }
    plan.copy_authored(bundle, "ost", &root.join("assets/ost"))?;
    let mut values = appearance.clone();
    let resolved = fonts.join("assets/reference-appearance.json");
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
        destination
            .file_name()
            .context("Missing destination filename")?
            .as_encoded_bytes(),
    )?;
    plan.output(cache, destination)
}

#[cfg(test)]
mod tests;
