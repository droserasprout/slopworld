//! Contract tests for validation, regular-face extraction and cache identity.
use super::*;

#[test]
fn invalid_unicode_and_rasterization_requests_are_rejected() {
    for value in [
        json!({"face":{}}),
        json!({"fallback":{"family":"mono","scalars":[0xd800]}}),
        json!({"fallback":{"family":"mono","scalars":[0x110000]}}),
        json!({"fallback":{"family":"mono","scalars":[true]}}),
        json!({"fallback":{"family":"mono","scalars":[]}}),
        json!({"fontSize":7}),
        json!({"density":5}),
        json!({"uiFontName":"x\n"}),
    ] {
        assert!(parse_request(&value).is_err(), "{value}");
    }
    let Request::Fallback { scalars, .. } =
        parse_request(&json!({"fallback":{"family":"mono","scalars":[66,65,66,0x1f642]}})).unwrap()
    else {
        panic!()
    };
    assert_eq!(scalars, vec![65, 66, 0x1f642]);
}
#[test]
fn coverage_ranges_preserve_holes_and_supplementary_scalars() {
    let ranges = opentype::compress(&[0, 1, 5, 6, 0x1f600, 0x10ffff]);
    assert_eq!(
        ranges,
        vec![[0, 1], [5, 6], [0x1f600, 0x1f600], [0x10ffff, 0x10ffff]]
    );
    for scalar in [0, 1, 5, 6, 0x1f600, 0x10ffff] {
        assert!(opentype::contains(&ranges, scalar));
    }
    for scalar in [2, 4, 7, 0x1f601] {
        assert!(!opentype::contains(&ranges, scalar));
    }
}
#[test]
fn unsupported_fonts_and_truncated_collections_fail_without_panicking() {
    for bytes in [
        b"not a font".as_slice(),
        b"ttcf",
        b"ttcf\0\x01\0\0\0\0\0\x01",
    ] {
        assert!(opentype::metadata(bytes, 0, true).is_err());
        if bytes.starts_with(b"ttcf") {
            assert!(opentype::extract(bytes, 0).is_err());
        }
    }
}
#[test]
fn collection_extraction_rebases_tables_and_repairs_checksum() {
    let mut bytes = vec![0u8; 60];
    bytes[..4].copy_from_slice(b"ttcf");
    bytes[8..12].copy_from_slice(&1u32.to_be_bytes());
    bytes[12..16].copy_from_slice(&16u32.to_be_bytes());
    bytes[16..20].copy_from_slice(&0x00010000u32.to_be_bytes());
    bytes[20..22].copy_from_slice(&1u16.to_be_bytes());
    bytes[28..32].copy_from_slice(b"head");
    bytes[36..40].copy_from_slice(&44u32.to_be_bytes());
    bytes[40..44].copy_from_slice(&16u32.to_be_bytes());
    bytes[44..48].copy_from_slice(&0x00010000u32.to_be_bytes());
    let extracted = opentype::extract(&bytes, 0).unwrap();
    assert_eq!(
        u32::from_be_bytes(extracted[20..24].try_into().unwrap()),
        28
    );
    assert_eq!(extracted.len(), 44);
    let sum = extracted
        .as_chunks::<4>()
        .0
        .iter()
        .fold(0u32, |sum, v| sum.wrapping_add(u32::from_be_bytes(*v)));
    assert_eq!(sum, 0xb1b0afba);
    assert!(opentype::extract(&bytes, 1).is_err());
}
#[test]
fn content_objects_reject_corruption_and_path_traversal() {
    let root = tempfile::tempdir().unwrap();
    let bytes = b"private font bytes";
    let hash = cache::digest(bytes);
    cache::write(&cache::object(root.path(), &hash), bytes).unwrap();
    assert_eq!(read_object(root.path(), &hash).unwrap(), bytes);
    cache::write(&cache::object(root.path(), &hash), b"corrupt").unwrap();
    assert!(read_object(root.path(), &hash).is_err());
    assert!(read_object(root.path(), "../../etc/passwd").is_err());
}
#[test]
fn file_identity_detects_same_length_replacement_and_removal() {
    let root = tempfile::tempdir().unwrap();
    let file = root.path().join("font");
    fs::write(&file, b"first").unwrap();
    let before = cache::stamp(&file);
    cache::write(&file, b"other").unwrap();
    assert_ne!(before, cache::stamp(&file));
    fs::remove_file(&file).unwrap();
    assert!(cache::stamp(&file).is_none());
}
#[test]
fn preload_uses_order_and_coverage_contribution_without_named_glyphs() {
    let mut catalog = Catalog::default();
    catalog.faces = vec![
        test_face("a", vec![[65, 66]]),
        test_face("b", vec![[65, 65]]),
        test_face("c", vec![[0x400, 0x410]]),
        test_face("d", vec![[0x1f600, 0x1f600]]),
    ];
    assert_eq!(
        preload(&catalog, &["a".into(), "b".into(), "c".into(), "d".into()]),
        vec!["a", "c", "d"]
    );
}
fn test_face(name: &str, coverage: Vec<[u32; 2]>) -> Face {
    Face {
        family: name.into(),
        mono: false,
        score: 0,
        hash: name.into(),
        byte_len: 1024,
        coverage,
    }
}

#[test]
#[ignore = "requires installed Fontconfig and system fonts"]
fn system_index_reuses_persistent_sources_and_resolves_without_game() {
    // Fontconfig is the Linux product dependency. No game, managed helper, browser
    // or graphics context is involved in exercising the discovery/cache path.
    let root = tempfile::tempdir().unwrap();
    let mut catalog = Catalog::default();
    catalog.rebuild(root.path()).unwrap();
    let generation = catalog.generation.clone();
    assert!(catalog.fresh(root.path()));
    let chain = catalog.chain(root.path(), "monospace").unwrap();
    assert!(!chain.is_empty());
    let loaded = Catalog::load(root.path());
    assert_eq!(loaded.generation, generation);
    assert!(loaded.fresh(root.path()));
    let first = loaded.faces.first().unwrap();
    let bytes = read_object(root.path(), &first.hash).unwrap();
    assert!(opentype::metadata(&bytes, 0, true).is_ok());
    fs::remove_file(cache::object(root.path(), &first.hash)).unwrap();
    assert!(!loaded.fresh(root.path()));
    catalog.rebuild(root.path()).unwrap();
    assert!(catalog.fresh(root.path()));
}

#[test]
fn preload_skips_large_faces_without_consuming_the_coverage_budget() {
    let mut catalog = Catalog::default();
    let mut large = test_face("large", vec![[65, 66]]);
    large.byte_len = 8 * 1024 * 1024;
    let small = test_face("small", vec![[65, 66]]);
    catalog.faces = vec![large, small];
    assert_eq!(
        preload(&catalog, &["large".into(), "small".into()]),
        vec!["small"]
    );
}

#[test]
fn regular_style_distance_and_cmap_come_from_opentype_tables() {
    let original = include_bytes!("../../../../assets/fonts/clacon2.ttf");
    let base = opentype::metadata(original, 0, true).unwrap();
    assert!(opentype::contains(&base.coverage, 'A' as u32));
    let mut bold_italic = original.to_vec();
    let count = u16::from_be_bytes(original[4..6].try_into().unwrap()) as usize;
    let record = (0..count)
        .map(|i| 12 + i * 16)
        .find(|&i| &original[i..i + 4] == b"OS/2")
        .unwrap();
    let offset = u32::from_be_bytes(original[record + 8..record + 12].try_into().unwrap()) as usize;
    bold_italic[offset + 4..offset + 6].copy_from_slice(&700u16.to_be_bytes());
    bold_italic[offset + 6..offset + 8].copy_from_slice(&5u16.to_be_bytes());
    bold_italic[offset + 62..offset + 64].copy_from_slice(&1u16.to_be_bytes());
    let alternate = opentype::metadata(&bold_italic, 0, true).unwrap();
    assert_eq!(alternate.family, base.family);
    assert_eq!(alternate.score, 100_300);
    assert!(base.score < alternate.score);
    assert_eq!(alternate.coverage, base.coverage);
    let post = (0..count)
        .map(|i| 12 + i * 16)
        .find(|&i| &original[i..i + 4] == b"post")
        .unwrap();
    let offset = u32::from_be_bytes(original[post + 8..post + 12].try_into().unwrap()) as usize;
    bold_italic[offset + 12..offset + 16].copy_from_slice(&1u32.to_be_bytes());
    assert!(opentype::metadata(&bold_italic, 0, false).unwrap().mono);
}

#[test]
fn persistent_catalog_detects_replaced_added_removed_and_unsupported_sources() {
    let work = tempfile::tempdir().unwrap();
    let root = work.path().join("cache");
    let installed = work.path().join("fonts");
    fs::create_dir(&root).unwrap();
    fs::create_dir(&installed).unwrap();
    let source = installed.join("regular.ttf");
    let unsupported = installed.join("broken.ttf");
    fs::write(
        &source,
        include_bytes!("../../../../assets/fonts/clacon2.ttf"),
    )
    .unwrap();
    fs::write(&unsupported, b"unsupported").unwrap();
    let mut catalog = Catalog::default();
    catalog
        .rebuild_inventory(
            &root,
            vec![source.clone(), unsupported.clone()],
            vec![installed.clone()],
        )
        .unwrap();
    assert_eq!(catalog.faces.len(), 1);
    assert!(catalog.fresh(&root));
    let generation = catalog.generation.clone();
    let reloaded = Catalog::load(&root);
    assert!(reloaded.fresh(&root));
    assert_eq!(reloaded.generation, generation);
    let file = cache::object(&root, &catalog.faces[0].hash);
    let object_stamp = cache::stamp(&file);
    catalog
        .rebuild_inventory(
            &root,
            vec![source.clone(), unsupported.clone()],
            vec![installed.clone()],
        )
        .unwrap();
    assert_eq!(object_stamp, cache::stamp(&file));
    let added = installed.join("new.ttf");
    fs::write(&added, b"new unsupported font").unwrap();
    assert!(!catalog.fresh(&root));
    catalog
        .rebuild_inventory(
            &root,
            vec![source.clone(), unsupported.clone(), added.clone()],
            vec![installed.clone()],
        )
        .unwrap();
    assert_ne!(catalog.generation, generation);
    assert!(catalog.fresh(&root));
    fs::remove_file(&added).unwrap();
    assert!(!catalog.fresh(&root));
    catalog
        .rebuild_inventory(
            &root,
            vec![source.clone(), unsupported.clone()],
            vec![installed.clone()],
        )
        .unwrap();
    cache::write(
        &source,
        include_bytes!("../../../../assets/fonts/clacon2.ttf"),
    )
    .unwrap();
    assert!(!catalog.fresh(&root));
    catalog
        .rebuild_inventory(&root, vec![source, unsupported], vec![installed])
        .unwrap();
    assert!(catalog.fresh(&root));
    fs::remove_file(file).unwrap();
    assert!(!catalog.fresh(&root));
}

fn fixture_service() -> (tempfile::TempDir, FontService) {
    let work = tempfile::tempdir().unwrap();
    let root = work.path().join("cache");
    let installed = work.path().join("fonts");
    fs::create_dir(&root).unwrap();
    fs::create_dir(&installed).unwrap();
    let source = installed.join("font.ttf");
    fs::write(
        &source,
        include_bytes!("../../../../assets/fonts/clacon2.ttf"),
    )
    .unwrap();
    let mut catalog = Catalog::default();
    catalog
        .rebuild_inventory(&root, vec![source], vec![installed])
        .unwrap();
    catalog.faces[0].family = "DejaVu Sans".into();
    catalog
        .chains
        .insert("monospace".into(), vec!["DejaVu Sans".into()]);
    let service = FontService {
        inner: Arc::new(Inner {
            root,
            flags: 513,
            state: Mutex::new(State {
                catalog,
                checked: Some(Instant::now()),
            }),
        }),
    };
    (work, service)
}

#[tokio::test]
async fn protocol_two_omits_binary_payloads_and_fallback_misses_are_explicit() {
    let (_work, service) = fixture_service();
    let selected = service.request(json!({"protocol":2})).await.unwrap();
    assert!(selected["fonts"].as_object().unwrap().is_empty());
    assert_eq!(selected["faces"].as_array().unwrap().len(), 4);
    let hash = selected["files"]["ui.ttf"].as_str().unwrap();
    assert_eq!(hash.len(), 64);
    let bytes = service.font_bytes(hash).await.unwrap();
    assert_eq!(cache::digest(&bytes), hash);
    let legacy = service.request(json!({})).await.unwrap();
    assert_eq!(
        STANDARD
            .decode(legacy["fonts"]["ui.ttf"].as_str().unwrap())
            .unwrap(),
        bytes
    );
    let reply = service
        .request(json!({"protocol":2,"fallback":{"family":"monospace","scalars":[65,0x10ffff]}}))
        .await
        .unwrap();
    assert_eq!(reply["assignments"]["65"], "face0.ttf");
    assert!(reply["assignments"]["1114111"].is_null());
    assert_eq!(reply["files"]["face0.ttf"], hash);
}

#[test]
fn startup_preserves_three_native_indices_when_sizes_collapse_and_normalizes_removed_ui() {
    let (work, service) = fixture_service();
    let output = work.path().join("runtime");
    let selected = service
        .prepare_startup(&output, json!({"uiFontName":"Removed Font","uiFontSize":1}))
        .unwrap();
    assert_eq!(selected["uiFontName"], "");
    let manifest: Value =
        serde_json::from_slice(&fs::read(output.join("assets/fonts/manifest.json")).unwrap())
            .unwrap();
    assert_eq!(manifest.as_array().unwrap().len(), 4);
    for index in 0..3 {
        assert_eq!(manifest[index]["index"], index);
        let path = output
            .join("assets/fonts")
            .join(manifest[index]["bake"].as_str().unwrap());
        let descriptor: Value = serde_json::from_slice(&fs::read(path).unwrap()).unwrap();
        assert_eq!(descriptor["size"], [1, 1, 3][index]);
    }
    let before = service.startup_fingerprint().unwrap();
    service.inner.state.lock().unwrap().catalog.generation =
        "changed source or config identity".into();
    assert_ne!(service.startup_fingerprint().unwrap(), before);
}

#[tokio::test]
async fn corrupted_content_download_recovers_from_cached_original_without_a_rescan() {
    let (_work, service) = fixture_service();
    let reply = service.request(json!({"protocol":2})).await.unwrap();
    let hash = reply["files"]["ui.ttf"].as_str().unwrap();
    cache::write(&cache::object(&service.inner.root, hash), b"broken object").unwrap();
    assert_eq!(
        cache::digest(&service.font_bytes(hash).await.unwrap()),
        hash
    );
}

#[test]
fn pruning_bounds_retired_bytes_and_count_and_never_evicts_current_objects() {
    let root = tempfile::tempdir().unwrap();
    fs::write(root.path().join("current"), vec![0; 100]).unwrap();
    for name in ["old-a", "old-b", "old-c"] {
        fs::write(root.path().join(name), vec![0; 4]).unwrap();
    }
    cache::prune_stale(root.path(), &HashSet::from(["current".into()]), 2, 6).unwrap();
    assert!(root.path().join("current").exists());
    assert_eq!(fs::read_dir(root.path()).unwrap().count(), 2);
    cache::prune_stale(root.path(), &HashSet::from(["current".into()]), 0, 0).unwrap();
    assert_eq!(fs::read_dir(root.path()).unwrap().count(), 1);
}

#[test]
fn directory_watches_follow_empty_font_symlinks_without_following_cycles() {
    let work = tempfile::tempdir().unwrap();
    let fonts = work.path().join("fonts");
    let target = work.path().join("target");
    let root = work.path().join("cache");
    fs::create_dir(&fonts).unwrap();
    fs::create_dir(&target).unwrap();
    fs::create_dir(&root).unwrap();
    std::os::unix::fs::symlink(&target, fonts.join("linked")).unwrap();
    std::os::unix::fs::symlink(&fonts, target.join("cycle")).unwrap();
    let source = fonts.join("regular.ttf");
    fs::write(
        &source,
        include_bytes!("../../../../assets/fonts/clacon2.ttf"),
    )
    .unwrap();
    let mut catalog = Catalog::default();
    catalog
        .rebuild_inventory(&root, vec![source], vec![fonts])
        .unwrap();
    assert!(catalog.fresh(&root));
    fs::write(target.join("new.ttf"), b"new").unwrap();
    assert!(!catalog.fresh(&root));
}
