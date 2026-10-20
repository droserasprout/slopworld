use super::*;

#[test]
fn overlapping_pollers_claim_each_catalog_once_without_delaying_other_catalogs() {
    let state = CatalogState::new(None, None);
    std::thread::scope(|scope| {
        let claims: Vec<_> = (0..8)
            .map(|_| scope.spawn(|| state.claim(Catalog::Config, 2_000)))
            .collect();
        assert_eq!(
            claims
                .into_iter()
                .map(|thread| thread.join().unwrap())
                .filter(|claimed| *claimed)
                .count(),
            1
        );
    });
    assert_eq!(state.deadline(2_000), 0);
    assert!(state.claim(Catalog::Presets, 2_000));
    assert!(state.claim(Catalog::Jukebox, 2_000));
    assert_eq!(state.deadline(2_000), 4_000);
    assert!(!state.claim(Catalog::Config, 3_999));
    assert!(state.claim(Catalog::Config, 4_000));
}
