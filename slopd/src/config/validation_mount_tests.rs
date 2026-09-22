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
    assert!(validate_mount_paths(&p).is_err());
}
