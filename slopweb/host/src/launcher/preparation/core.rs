//! Partition Core data by native language folder, retaining English fallback and discovery.
use super::files::{self, Inputs};
use anyhow::{Result, ensure};
use std::{
    collections::{BTreeMap, BTreeSet},
    fs::{self, File},
    io,
    path::Path,
};
use zip::{CompressionMethod, ZipWriter, write::SimpleFileOptions};

const LANGUAGES: &str = "Data/Core/Languages/";

fn add_files(archive: &mut ZipWriter<File>, files: Inputs) -> Result<()> {
    let options = SimpleFileOptions::default()
        .compression_method(CompressionMethod::Deflated)
        .unix_permissions(0o644);
    for (name, path) in files {
        archive.start_file(name, options)?;
        io::copy(&mut File::open(path)?, archive)?;
    }
    Ok(())
}

pub(in crate::launcher) fn core_archive(game: &Path, destination: &Path) -> Result<()> {
    let inputs = files::inputs(game, &["Data/Core", "Rendering/Shaders"])?;
    let mut base = Inputs::new();
    let mut locales: BTreeMap<String, Inputs> = BTreeMap::new();
    for (name, path) in inputs {
        ensure!(
            !path.is_symlink()
                && !path
                    .extension()
                    .is_some_and(|x| x.eq_ignore_ascii_case("dll")),
            "Core-only staging rejects {}",
            path.display()
        );
        if let Some(relative) = name.strip_prefix(LANGUAGES)
            && let Some(folder) = relative
                .split_once('/')
                .map(|(folder, _)| folder)
                .or_else(|| relative.strip_suffix(".tar"))
            && !folder.is_empty()
        {
            locales
                .entry(folder.to_owned())
                .or_default()
                .insert(relative.to_owned(), path);
        } else {
            base.insert(name, path);
        }
    }

    let mut archive = ZipWriter::new(File::create(destination)?);
    let has_english = locales.contains_key("English");
    // Verse merges modern names with legacy folders while enumerating. Preserve
    // native directory/tar ordering and data availability for those pairs.
    let mut bundled = BTreeSet::from(["English".to_owned()]);
    for folder in locales.keys() {
        if let Some((legacy, _)) = folder.split_once(" (")
            && locales.contains_key(legacy)
        {
            bundled.insert(legacy.to_owned());
            bundled.insert(folder.clone());
        }
    }
    let packs = destination.with_file_name("locales");
    for (folder, members) in locales {
        // Without English the native loader can choose the first available language.
        // Keep both representations together when an installation has loose overrides
        // alongside its tar; native AbstractFilesystem determines their precedence.
        let tar = format!("{folder}.tar");
        if !has_english
            || bundled.contains(&folder)
            || (members.len() > 1 && members.contains_key(&tar))
            || base.contains_key(&format!("{LANGUAGES}{folder}"))
        {
            base.extend(
                members
                    .into_iter()
                    .map(|(name, path)| (format!("{LANGUAGES}{name}"), path)),
            );
            continue;
        }
        archive.add_directory(
            format!("{LANGUAGES}{folder}/"),
            SimpleFileOptions::default(),
        )?;
        fs::create_dir_all(&packs)?;
        let mut pack = ZipWriter::new(File::create(packs.join(format!("{folder}.zip")))?);
        add_files(&mut pack, members)?;
        pack.finish()?;
    }
    add_files(&mut archive, base)?;
    archive.finish()?;
    Ok(())
}

#[cfg(test)]
mod tests;
