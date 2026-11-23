//! Persisted fallback admission and policy invalidation contracts.
use super::*;

#[test]
fn unrelated_directory_entries_do_not_change_published_font_identity() {
    let work = tempfile::tempdir().unwrap();
    let fonts = work.path().join("fonts");
    fs::create_dir(&fonts).unwrap();
    let source = fonts.join("regular.ttf");
    fs::write(
        &source,
        include_bytes!("../../../../assets/fonts/clacon2.ttf"),
    )
    .unwrap();
    let root = work.path().join("cache");
    let inputs = || {
        (
            vec![source.clone()],
            vec![fonts.clone(), work.path().join(".fonts")],
        )
    };
    let mut catalog = Catalog::default();
    catalog.rebuild_from(&root, || Ok(inputs())).unwrap();
    let generation = catalog.generation.clone();
    fs::write(work.path().join("ordinary-file"), []).unwrap();
    assert!(catalog.fresh(&root));
    fs::write(fonts.join("readme.txt"), []).unwrap();
    assert!(!catalog.fresh(&root));
    catalog.rebuild_from(&root, || Ok(inputs())).unwrap();
    assert_eq!(catalog.generation, generation);
    fs::create_dir(work.path().join(".fonts")).unwrap();
    assert!(!catalog.fresh(&root));
}

#[test]
fn installation_between_enumeration_and_stamp_capture_cannot_publish_a_stale_catalog() {
    let work = tempfile::tempdir().unwrap();
    let root = work.path().join("cache");
    let fonts = work.path().join("fonts");
    fs::create_dir(&fonts).unwrap();
    let source = fonts.join("regular.ttf");
    let added = fonts.join("another.ttf");
    fs::write(
        &source,
        include_bytes!("../../../../assets/fonts/clacon2.ttf"),
    )
    .unwrap();
    let mut catalog = Catalog::default();
    catalog
        .rebuild_inventory(
            &root,
            std::slice::from_ref(&source),
            std::slice::from_ref(&fonts),
        )
        .unwrap();
    let generation = catalog.generation.clone();
    let result = catalog.rebuild_from(&root, || {
        let mut files = vec![source.clone()];
        if added.is_file() {
            files.push(added.clone());
        } else {
            // The enumerator already collected its result when the install completes.
            fs::copy(&source, &added)?;
        }
        Ok((files, vec![fonts.clone()]))
    });
    assert!(
        result
            .unwrap_err()
            .to_string()
            .contains("changed during discovery")
    );
    assert_eq!(Catalog::load(&root).generation, generation);
    assert!(!catalog.fresh(&root));
    catalog
        .rebuild_inventory(&root, &[source, added], &[fonts])
        .unwrap();
    assert_ne!(catalog.generation, generation);
    assert!(catalog.fresh(&root));
}

#[test]
fn excluded_catalog_faces_stay_out_of_fallbacks_after_reload() {
    let root = tempfile::tempdir().unwrap();
    let mut catalog = Catalog::default();
    for family in ["Primary", "Fallback", "Color Emoji"] {
        catalog.faces.push(Face {
            family: family.into(),
            mono: false,
            score: 0,
            hash: family.into(),
            byte_len: 1024,
            coverage: vec![[65, 66]],
        });
    }
    let chain = catalog
        .chain_with_order(root.path(), "primary", |_| {
            Ok(vec![
                "PRIMARY".into(),
                "Unavailable".into(),
                "Fallback".into(),
                "primary".into(),
            ])
        })
        .unwrap();
    assert_eq!(chain, vec!["Primary", "Fallback"]);
    let mut reloaded = Catalog::load(root.path());
    assert_eq!(
        reloaded
            .chain_with_order(root.path(), "primary", |_| panic!("reuse persisted chain"))
            .unwrap(),
        chain
    );
}

#[test]
fn previous_fallback_policy_invalidates_persisted_catalog() {
    let work = tempfile::tempdir().unwrap();
    let root = work.path().join("cache");
    fs::create_dir(&root).unwrap();
    let font = work.path().join("regular.ttf");
    fs::write(
        &font,
        include_bytes!("../../../../assets/fonts/clacon2.ttf"),
    )
    .unwrap();
    let mut catalog = Catalog::default();
    catalog.rebuild_inventory(&root, &[font], &[]).unwrap();
    assert!(catalog.fresh(&root));
    catalog.schema = 1;
    catalog
        .chains
        .insert("primary".into(), vec!["Color Emoji".into()]);
    catalog.save(&root).unwrap();
    assert!(!Catalog::load(&root).fresh(&root));
}
