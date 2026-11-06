use super::*;

fn grant(sessions: &[&str], level: Level) -> AgentCredential {
    AgentCredential {
        grantor: "g".into(),
        sessions: sessions.iter().map(|s| s.to_string()).collect(),
        level,
        revoked: Default::default(),
    }
}

#[test]
fn identity_credentials_supply_self_access_and_ignore_legacy_scopes() {
    for level in [Level::Ro, Level::Rw] {
        let cap = Cap::Scoped(grant(&["other", "host"], level));
        assert!(cap.allows("g", false, Level::Rw));
        assert!(!cap.allows("other", false, Level::Ro));
        assert!(!cap.allows("g", true, Level::Ro));
        assert!(!cap.may_create());
    }
    assert!(Cap::Root.allows("host", true, Level::Rw));
}

#[test]
fn credentials_are_unique_and_blank_root_fails_closed() {
    let mut store = CredentialStore::default();
    let a = store.mint_launch("g", "identity").unwrap();
    let b = store.mint_launch("g", "identity").unwrap();
    assert_ne!(a, b);
    assert_eq!(a.len(), 32);
    for root in ["", " ", "\t\n"] {
        assert!(store.resolve(Some(&a), root).is_none());
        assert!(store.resolve(Some(root), root).is_none());
    }
    assert!(store.resolve(None, "root").is_none());
    assert!(store.resolve(Some("unknown"), "root").is_none());
    assert!(matches!(
        store.resolve(Some("root"), "root"),
        Some(Cap::Root)
    ));
}

#[test]
fn persisted_credentials_bind_identity_and_revocation_reaches_resolved_copies() {
    let root = crate::test_support::fixture("identity-credentials");
    let mut store = CredentialStore::load_data(&root).unwrap();
    let token = store.mint_launch("g", "identity").unwrap();
    let text = fs::read_to_string(root.join("credentials.toml")).unwrap();
    assert!(!text.contains("sessions"));
    assert!(!text.contains("level"));
    let mut store = CredentialStore::load_data(&root).unwrap();
    store
        .prune_stale(&[("g".into(), "identity".into())].into(), &HashSet::new())
        .unwrap();
    let cap = store.resolve(Some(&token), "root").unwrap();
    store
        .prune_stale(
            &[("g".into(), "replacement".into())].into(),
            &HashSet::new(),
        )
        .unwrap();
    assert!(!cap.is_valid());
    assert!(
        CredentialStore::load_data(&root)
            .unwrap()
            .resolve(Some(&token), "root")
            .is_none()
    );
}

#[test]
fn old_grants_are_never_restored_and_absent_processes_lose_credentials() {
    let root = crate::test_support::fixture("retired-grants");
    fs::write(
        root.join("grants.toml"),
        "old grant data is deliberately not read",
    )
    .unwrap();
    let mut store = CredentialStore::load_data(&root).unwrap();
    assert_eq!(store.count(), 0);
    let token = store.mint_launch("g", "identity").unwrap();
    let cap = store.resolve(Some(&token), "root").unwrap();
    store.prune_absent_launches(&HashSet::new()).unwrap();
    assert!(!cap.is_valid());
    assert!(!store.revoke_launch("g").unwrap());
}

#[test]
fn entropy_failure_never_yields_a_credential() {
    gen_token_from(std::io::empty()).unwrap_err();
}

#[test]
fn revocation_save_failure_invalidates_live_caps_and_reports_disk_cleanup() {
    for cleanup in ["present", "absent", "blocked"] {
        let root = crate::test_support::fixture("grant-revoke-failure");
        let path = root.join("credentials.toml");
        let mut grants = CredentialStore::load_data(&root).unwrap();
        let token = grants
            .mint_persisted(
                grant(&["a"], Level::Rw),
                "1111111111111111".into(),
                BTreeMap::from([("a".into(), "2222222222222222".into())]),
            )
            .unwrap();
        let cap = grants.resolve(Some(&token), "root").unwrap();
        if cleanup != "present" {
            fs::remove_file(&path).unwrap();
        }
        if cleanup == "blocked" {
            fs::create_dir(&path).unwrap();
        }
        let fault = crate::paths::fail_writes(&path);
        let error = grants.try_revoke_grantor("g").unwrap_err();
        let message = format!("{error:#}");
        assert!(message.contains("injected persistence failure"));
        assert!(!cap.is_valid());
        assert!(!cap.allows("a", false, Level::Ro));
        assert!(grants.resolve(Some(&token), "root").is_none());
        assert!(!grants.try_revoke_grantor("g").unwrap());
        drop(fault);
        if cleanup == "blocked" {
            assert!(message.contains("could not be cleared"));
            assert!(CredentialStore::load_data(&root).is_err());
        } else {
            assert!(message.contains(if cleanup == "present" {
                "store cleared"
            } else {
                "store absent"
            }));
            assert!(
                CredentialStore::load_data(&root)
                    .unwrap()
                    .resolve(Some(&token), "root")
                    .is_none()
            );
        }
    }
}
