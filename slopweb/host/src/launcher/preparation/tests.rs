//! Order live install changes relative to phase capture, publication, and retry.
use super::*;
use std::io::Read;

struct Fixture {
    _temp: tempfile::TempDir,
    bundle: files::PinnedBundle,
    game: PathBuf,
    cache: PathBuf,
}

impl Fixture {
    fn new() -> Self {
        let temp = tempfile::tempdir().unwrap();
        let bundle = temp.path().join("bundle");
        fs::create_dir_all(bundle.join("web")).unwrap();
        fs::create_dir(bundle.join("ost")).unwrap();
        let game = temp.path().join("game");
        fs::create_dir_all(game.join("Data/Core")).unwrap();
        fs::write(game.join("Data/Core/base.xml"), "old").unwrap();
        ZipWriter::new(File::create(game.join("Assets.zip")).unwrap())
            .finish()
            .unwrap();
        let cache = temp.path().join("cache");
        Self {
            _temp: temp,
            bundle: files::PinnedBundle::open(bundle).unwrap(),
            game,
            cache,
        }
    }

    fn run(&self, build: impl Fn(Phase, &Path, &Path) -> Result<()>) -> Result<PathBuf> {
        prepare(
            &self.bundle,
            &self.game,
            &self.cache,
            false,
            &Map::new(),
            "fonts",
            |phase, inputs, output, _| build(phase, inputs, output),
        )
    }
}

#[test]
fn changed_bytes_before_capture_cannot_publish_a_cold_component() {
    let f = Fixture::new();
    let source = f.game.join("Data/Core/base.xml");
    let error = f
        .run(|phase, _, _| {
            assert_eq!(phase, Phase::Managed);
            fs::write(&source, "new")?;
            Ok(())
        })
        .unwrap_err();
    assert!(
        error
            .to_string()
            .contains("game/Data/Core/base.xml changed")
    );
    assert_eq!(
        fs::read_dir(f.cache.join("prepared-components/assets"))
            .unwrap()
            .count(),
        0
    );

    fs::write(source, "old").unwrap();
    let runtime = f
        .run(|phase, inputs, root| {
            assert_ne!(phase, Phase::Managed);
            if phase == Phase::Assets {
                fs::copy(inputs.join("game/Data/Core/base.xml"), root.join("derived"))?;
            }
            Ok(())
        })
        .unwrap();
    assert_eq!(fs::read(runtime.join("derived")).unwrap(), b"old");
}

#[test]
fn interrupted_composition_can_reuse_a_phase_built_during_a_transient_update() {
    let f = Fixture::new();
    let source = f.game.join("Data/Core/base.xml");
    let added = f.game.join("Data/Core/extra.xml");
    let error = f
        .run(|phase, inputs, root| {
            match phase {
                Phase::Managed => fs::write(&added, "unplanned")?,
                Phase::Assets => {
                    fs::write(&source, "new")?;
                    phase.build(inputs, root, || anyhow::bail!("unexpected font build"))?;
                    fs::write(&source, "old")?;
                    fs::remove_file(&added)?;
                }
                Phase::Fonts => anyhow::bail!("interrupted after assets publication"),
                Phase::Background => anyhow::bail!("unexpected background build"),
            }
            Ok(())
        })
        .unwrap_err();
    assert_eq!(error.to_string(), "interrupted after assets publication");
    // There are no active pointers yet. Retry discovers the cold components
    // directly by key, including the one built while the live bytes differed.
    let runtime = f
        .run(|phase, _, _| {
            assert!(matches!(phase, Phase::Fonts | Phase::Background));
            Ok(())
        })
        .unwrap();
    let mut archive =
        zip::ZipArchive::new(File::open(runtime.join("assets/Core.zip")).unwrap()).unwrap();
    assert_eq!(archive.len(), 1);
    let mut base = String::new();
    archive
        .by_name("Data/Core/base.xml")
        .unwrap()
        .read_to_string(&mut base)
        .unwrap();
    assert_eq!(base, "old");
}
