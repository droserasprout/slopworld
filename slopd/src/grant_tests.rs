use super::*;

fn grant(sessions: &[&str], level: Level) -> Grant {
    Grant {
        grantor: "g".into(),
        sessions: sessions.iter().map(|s| s.to_string()).collect(),
        level,
        revoked: Default::default(),
    }
}

/// rw is a superset of ro, and ro is not rw.
#[test]
fn a_level_satisfies_itself_and_rw_satisfies_ro() {
    assert!(Level::Rw.satisfies(Level::Rw));
    assert!(Level::Rw.satisfies(Level::Ro));
    assert!(Level::Ro.satisfies(Level::Ro));
    assert!(!Level::Ro.satisfies(Level::Rw));
}

/// The heart of the model: a scoped grant reaches a session it names at its level, reaches
/// nothing it does not, and reaches no host session at all. Root reaches everything.
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

    // Root is unconditional, host included - it is the mod.
    assert!(Cap::Root.allows("anything", true, Level::Rw));
    assert!(Cap::Root.can_see("anything", true));
    assert!(Cap::Root.may_create());
    assert!(!Cap::Scoped(grant(&["a"], Level::Rw)).may_create());
}

/// Empty root is no-auth for anyone; a set root matches itself; anything else must name a
/// live grant; a wrong or absent token under a set root is nobody.
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
    match grants.resolve(Some(&tok), "root") {
        Some(Cap::Scoped(g)) => assert!(g.sessions.contains("a")),
        other => panic!("expected a scoped cap, got {other:?}"),
    }
}

/// The ephemeral contract: a grantor going away takes its grants with it, and nobody
/// else's.
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

/// Two mints do not collide, and a token is the width we asked for.
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
