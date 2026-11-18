//! Persisted fallback admission and policy invalidation contracts.
use super::*;

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
    catalog
        .rebuild_inventory(&root, vec![font], vec![])
        .unwrap();
    assert!(catalog.fresh(&root));
    catalog.schema = 1;
    catalog
        .chains
        .insert("primary".into(), vec!["Color Emoji".into()]);
    catalog.save(&root).unwrap();
    assert!(!Catalog::load(&root).fresh(&root));
}
