use super::*;
use crate::config::{Mount, MountMode};

#[test]
fn path_mounts_reject_empty_relative_protected_and_duplicate_targets() {
    let mut p = ProjectCfg {
        name: "repo".into(),
        dir: "/work/repo".into(),
        ..Default::default()
    };
    for (from, to) in [
        ("", "/mnt/extra"),
        ("relative", "/mnt/extra"),
        ("/", "/mnt/extra"),
        ("/work/extra", "/"),
        ("/work/extra", "/work/repo"),
        ("/work/extra", "/mnt/../repo"),
    ] {
        p.mounts = vec![Mount {
            from: from.into(),
            to: to.into(),
            mode: MountMode::Rw,
        }];
        assert!(validate_mount_paths(&p).is_err(), "accepted {from} -> {to}");
    }
    p.mounts = vec![Mount {
        from: "/work/extra".into(),
        to: "/mnt/extra".into(),
        mode: MountMode::Ro,
    }];
    validate_mount_paths(&p).unwrap();
    p.mounts.push(p.mounts[0].clone());
    assert!(validate_mount_paths(&p).is_err());

    p.mounts = vec![Mount {
        from: "/work/extra".into(),
        to: "relative".into(),
        mode: MountMode::Ro,
    }];
    validate_mount_paths(&p).expect("relative destination uses the project directory");
    p.mounts.push(Mount {
        from: "/work/other".into(),
        to: "/work/repo/relative".into(),
        mode: MountMode::Rw,
    });
    let absolute = p.mounts[1].clone();
    let mut absolute_only = p.clone();
    absolute_only.mounts = vec![absolute];
    validate_mount_paths(&absolute_only).unwrap();
    assert!(validate_mount_paths(&p).is_err());
}

#[test]
fn relative_cache_links_cannot_nest_mount_destinations() {
    let mut p = ProjectCfg {
        name: "repo".into(),
        dir: "/work/repo".into(),
        mounts: vec![
            Mount {
                from: "/work/cache".into(),
                to: "build".into(),
                mode: MountMode::Cache,
            },
            Mount {
                from: "/work/other".into(),
                to: "build/output".into(),
                mode: MountMode::Rw,
            },
        ],
        ..Default::default()
    };
    assert!(
        validate_mount_paths(&p)
            .unwrap_err()
            .to_string()
            .contains("cannot overlap")
    );
    p.mounts.reverse();
    assert!(
        validate_mount_paths(&p)
            .unwrap_err()
            .to_string()
            .contains("cannot overlap")
    );
}
