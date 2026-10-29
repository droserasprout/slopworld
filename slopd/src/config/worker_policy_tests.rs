use super::*;

#[test]
fn saved_named_permissions_load_without_gaining_authority_and_write_as_fields() {
    for (name, expected) in [
        ("observer", WorkerAccess::none()),
        ("supervisor", WorkerAccess::default()),
        ("operator", WorkerAccess::all()),
    ] {
        let policy: WorkerPolicy = toml::from_str(&format!("access = '{name}'")).unwrap();
        assert_eq!(policy.access, expected);
        let saved = toml::to_string(&policy).unwrap();
        assert!(!saved.contains(name));
        assert_eq!(toml::from_str::<WorkerPolicy>(&saved).unwrap(), policy);
    }
    for invalid in [r#""unknown""#, r#"{"typo":true}"#, r#"{"terminal":"yes"}"#] {
        serde_json::from_str::<WorkerAccess>(invalid).unwrap_err();
    }
}

#[test]
fn daemon_ceiling_intersects_each_permission_independently() {
    let requested = WorkerAccess {
        followup: true,
        cancel: false,
        terminal: true,
        lifecycle: true,
    };
    let ceiling = WorkerAccess {
        followup: false,
        cancel: true,
        terminal: true,
        lifecycle: false,
    };
    assert_eq!(
        requested.intersect(ceiling),
        WorkerAccess {
            terminal: true,
            ..WorkerAccess::none()
        }
    );
}

#[test]
fn independent_permissions_round_trip_without_enabling_other_actions() {
    for bits in 0..16 {
        let access = WorkerAccess {
            followup: bits & 1 != 0,
            cancel: bits & 2 != 0,
            terminal: bits & 4 != 0,
            lifecycle: bits & 8 != 0,
        };
        let text = toml::to_string(&access).unwrap();
        assert_eq!(toml::from_str::<WorkerAccess>(&text).unwrap(), access);
        assert_eq!(
            serde_json::from_value::<WorkerAccess>(serde_json::to_value(access).unwrap()).unwrap(),
            access
        );
    }
}
