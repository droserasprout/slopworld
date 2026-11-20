//! Core fallback, locale layout, and staging admission contracts.
use super::*;
use std::io::Read;
use zip::ZipArchive;

fn write(root: &Path, name: &str, data: &str) {
    let path = root.join(name);
    fs::create_dir_all(path.parent().unwrap()).unwrap();
    fs::write(path, data).unwrap();
}

fn contents(path: &Path) -> BTreeMap<String, String> {
    let mut archive = ZipArchive::new(File::open(path).unwrap()).unwrap();
    let mut entries = BTreeMap::new();
    for i in 0..archive.len() {
        let mut file = archive.by_index(i).unwrap();
        let mut data = String::new();
        file.read_to_string(&mut data).unwrap();
        entries.insert(file.name().to_owned(), data);
    }
    entries
}

#[test]
fn core_archive_keeps_fallback_and_names_and_delivers_each_selected_layout() {
    let temp = tempfile::tempdir().unwrap();
    let game = temp.path().join("game");
    let languages = game.join(LANGUAGES);
    let core = temp.path().join("Core.zip");
    write(&languages, "English/Keyed/base.xml", "fallback");
    write(&languages, "French (Français).tar", "tar contents");
    write(&languages, "Custom/Keyed/strings.xml", "loose contents");
    write(&languages, "Custom/LanguageInfo.xml", "metadata");
    write(&languages, "Legacy/LanguageInfo.xml", "old folder");
    write(&languages, "Legacy (Native).tar", "modern tar");
    write(&languages, "Mixed.tar", "tar preferred by Verse");
    write(&languages, "Mixed/LanguageInfo.xml", "loose override");
    write(&languages, "Readme.txt", "note");
    core_archive(&game, &core).unwrap();
    let base = contents(&core);
    assert_eq!(base.len(), 8);
    assert_eq!(
        base["Data/Core/Languages/Legacy (Native).tar"],
        "modern tar"
    );
    assert_eq!(
        base["Data/Core/Languages/Legacy/LanguageInfo.xml"],
        "old folder"
    );
    assert_eq!(
        base["Data/Core/Languages/English/Keyed/base.xml"],
        "fallback"
    );
    assert_eq!(base["Data/Core/Languages/Readme.txt"], "note");
    assert_eq!(base["Data/Core/Languages/French (Français)/"], "");
    assert_eq!(base["Data/Core/Languages/Custom/"], "");
    assert_eq!(
        base["Data/Core/Languages/Mixed.tar"],
        "tar preferred by Verse"
    );
    assert_eq!(
        base["Data/Core/Languages/Mixed/LanguageInfo.xml"],
        "loose override"
    );
    let packs = temp.path().join("locales");
    assert_eq!(
        contents(&packs.join("French (Français).zip"))["French (Français).tar"],
        "tar contents"
    );
    let loose = contents(&packs.join("Custom.zip"));
    assert_eq!(loose["Custom/Keyed/strings.xml"], "loose contents");
    assert_eq!(loose["Custom/LanguageInfo.xml"], "metadata");
    write(&languages, "Custom/unsafe.DLL", "dll");
    assert!(core_archive(&game, &core).is_err());
}

#[test]
fn core_archive_keeps_native_first_language_fallback_without_english() {
    let temp = tempfile::tempdir().unwrap();
    write(
        temp.path(),
        "Data/Core/Languages/Custom.tar",
        "native fallback",
    );
    let core = temp.path().join("Core.zip");
    core_archive(temp.path(), &core).unwrap();
    assert_eq!(
        contents(&core)["Data/Core/Languages/Custom.tar"],
        "native fallback"
    );
    assert!(!temp.path().join("locales").exists());
}
