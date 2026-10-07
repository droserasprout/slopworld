use super::*;

fn grant(sessions: &[&str], level: Level) -> Grant {
    Grant {
        grantor: "g".into(),
        sessions: sessions.iter().map(|s| s.to_string()).collect(),
        level,
        revoked: Default::default(),
    }
}

/// Read-write access includes read-only access. Read-only access does not permit writes.
#[test]
fn a_level_satisfies_itself_and_rw_satisfies_ro() {
    assert!(Level::Rw.satisfies(Level::Rw));
    assert!(Level::Rw.satisfies(Level::Ro));
    assert!(Level::Ro.satisfies(Level::Ro));
    assert!(!Level::Ro.satisfies(Level::Rw));
}

/// A scoped grant permits access only to named sessions at its assigned level.
/// Scoped grants never permit host-session access. Root access has no such restrictions.
#[test]
fn a_scoped_grant_reaches_only_what_it_names_and_never_the_host() {
    let ro = Cap::Scoped(grant(&["a", "b"], Level::Ro));
    assert!(ro.allows("a", false, Level::Ro));
    assert!(!ro.allows("a", false, Level::Rw)); // ro cannot drive
    assert!(!ro.allows("c", false, Level::Ro)); // not in scope
    assert!(!ro.allows("a", true, Level::Ro)); // host, even if named

    let rw = Cap::Scoped(grant(&["a"], Level::Rw));
    assert!(rw.allows("a", false, Level::Rw));
    assert!(rw.allows("a", false, Level::Ro));
    assert!(!rw.allows("a", true, Level::Rw)); // still never the host

    // Root access permits all operations, including host-session access.
    assert!(Cap::Root.allows("anything", true, Level::Rw));
    assert!(Cap::Root.can_see("anything", true));
    assert!(Cap::Root.may_create());
    assert!(!Cap::Scoped(grant(&["a"], Level::Rw)).may_create());
}

/// An empty root token disables authentication and gives all callers root access.
/// Otherwise, accept the root token or a current grant token.
/// Reject absent or unknown tokens when authentication is enabled.
#[test]
fn resolving_a_token_against_root_and_the_grants() {
    let mut grants = Grants::default();
    assert!(matches!(grants.resolve(None, ""), Some(Cap::Root)));
    assert!(matches!(grants.resolve(Some("x"), ""), Some(Cap::Root)));

    assert!(matches!(
        grants.resolve(Some("root"), "root"),
        Some(Cap::Root)
    ));
    assert!(grants.resolve(Some("nope"), "root").is_none());
    assert!(grants.resolve(None, "root").is_none());

    let tok = grants.mint(grant(&["a"], Level::Ro));
    assert_ne!(tok, "root");
    let cap = grants.resolve(Some(&tok), "root").unwrap();
    assert!(cap.allows("a", false, Level::Ro));
    assert!(!cap.allows("a", false, Level::Rw));
    match Some(cap) {
        Some(Cap::Scoped(g)) => assert!(g.sessions.contains("a")),
        other => panic!("expected a scoped cap, got {other:?}"),
    }
}

/// Revoking a grantor removes only that grantor's grants.
#[test]
fn revoking_a_grantor_drops_its_grants_alone() {
    let mut grants = Grants::default();
    let a = grants.mint(Grant {
        grantor: "alice".into(),
        sessions: HashSet::new(),
        level: Level::Ro,
        revoked: Default::default(),
    });
    let b = grants.mint(Grant {
        grantor: "bob".into(),
        sessions: HashSet::new(),
        level: Level::Ro,
        revoked: Default::default(),
    });

    grants.revoke_grantor("alice");
    assert!(grants.resolve(Some(&a), "root").is_none());
    assert!(grants.resolve(Some(&b), "root").is_some());
    assert_eq!(grants.count(), 1);
}

/// Generated tokens differ and have the required length.
#[test]
fn a_minted_token_is_distinct_and_wide() {
    let mut grants = Grants::default();
    let a = grants.mint(grant(&[], Level::Ro));
    let b = grants.mint(grant(&[], Level::Ro));
    assert_ne!(a, b);
    assert_eq!(a.len(), 32); // 16 bytes, hex
}

#[test]
fn losing_a_target_revokes_the_whole_grant_and_its_resolved_copies() {
    let mut grants = Grants::default();
    let target = grants.mint(Grant {
        grantor: "alice".into(),
        sessions: ["gone", "stays"].into_iter().map(str::to_string).collect(),
        level: Level::Rw,
        revoked: Default::default(),
    });
    let unrelated = grants.mint(Grant {
        grantor: "bob".into(),
        sessions: ["stays"].into_iter().map(str::to_string).collect(),
        level: Level::Ro,
        revoked: Default::default(),
    });

    let cap = grants.resolve(Some(&target), "root").unwrap();
    assert!(cap.allows("stays", false, Level::Rw));
    assert!(grants.invalidate_session("gone"));
    assert!(!cap.is_valid());
    assert!(!cap.allows("gone", false, Level::Ro));
    assert!(!cap.allows("stays", false, Level::Ro));
    assert!(grants.resolve(Some(&target), "root").is_none());
    assert!(grants.resolve(Some(&unrelated), "root").is_some());
}
#[test]
fn entropy_failure_never_yields_a_grant_token() {
    let mut short = &[0u8; 8][..];
    super::gen_token_from(&mut short).unwrap_err();
}

#[test]
fn data_store_retains_tokens_and_rejects_competing_legacy_authority() {
    let root = std::env::temp_dir().join(format!("slopd-grant-layout-{}", uuid::Uuid::new_v4()));
    let config = root.join("config/config.toml");
    let data = root.join("data");
    fs::create_dir_all(config.parent().unwrap()).unwrap();
    fs::create_dir_all(&data).unwrap();
    let mut legacy = Grants::load(&config).unwrap();
    let token = legacy
        .mint_persisted(
            grant(&["a"], Level::Rw),
            "1111111111111111".into(),
            BTreeMap::from([("a".into(), "2222222222222222".into())]),
        )
        .unwrap();
    let old = config.with_file_name("grants.toml");
    let bytes = fs::read(&old).unwrap();
    assert!(Grants::load_data(&config, &data).is_err());
    fs::write(data.join("grants.toml"), &bytes).unwrap();
    assert!(Grants::load_data(&config, &data).is_err());
    fs::remove_file(&old).unwrap();
    let mut selected = Grants::load_data(&config, &data).unwrap();
    assert!(
        selected
            .resolve(Some(&token), "root")
            .unwrap()
            .allows("a", false, Level::Rw)
    );
    assert_eq!(fs::read(data.join("grants.toml")).unwrap(), bytes);
    selected.try_revoke_grantor("g").unwrap();
    assert!(
        Grants::load_data(&config, &data)
            .unwrap()
            .resolve(Some(&token), "root")
            .is_none()
    );
    assert!(!old.exists());
    fs::remove_dir_all(root).unwrap();
}
