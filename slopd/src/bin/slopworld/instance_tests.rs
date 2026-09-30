use super::super::tests::scratch;
use super::*;

#[test]
fn launcher_lock_rejects_a_second_owner_and_reopens_after_drop() {
    let p = scratch("lock").join("launcher.lock");
    let first = InstanceLock::acquire(&p).expect("first launcher owns the lock");
    let second = InstanceLock::acquire(&p);
    let err = match second {
        Ok(_) => panic!("second launcher must be rejected"),
        Err(e) => e,
    };
    assert!(err.contains("another SlopWorld session"));

    drop(first);
    InstanceLock::acquire(&p).expect("the kernel releases the lock after the owner exits");
    if let Some(parent) = p.parent() {
        drop(std::fs::remove_dir_all(parent));
    }
}

#[test]
fn savedatafolder_is_read_from_the_argv() {
    let pinned: &[u8] = b"/g/RimWorldLinux\0-savedatafolder=/p\0-popupwindow\0";
    assert_eq!(savedatafolder_of(pinned), Some(PathBuf::from("/p")));
    let vanilla: &[u8] = b"/g/RimWorldLinux\0-popupwindow\0";
    assert_eq!(savedatafolder_of(vanilla), None);
}

#[test]
fn the_launcher_lock_is_keyed_on_the_profile() {
    let native = launcher_lock_path(Path::new("/data/profile"));
    let sidecar = launcher_lock_path(Path::new("/data/profile-slopcar"));
    assert_ne!(native, sidecar, "different profiles must not share a lock");
    assert_eq!(
        native,
        launcher_lock_path(Path::new("/data/profile")),
        "one profile must map to one lock"
    );
    assert_eq!(
        native.parent(),
        sidecar.parent(),
        "both locks live under the same slopworld directory"
    );
    assert_eq!(
        lock_file_name(Path::new("/data/new-profile")),
        lock_file_name(Path::new("/data/missing/../new-profile")),
        "equivalent absent profiles must share the race-prevention lock"
    );
}

#[test]
fn missing_profile_under_symlink_keeps_lock_identity_after_seeding() {
    let root = scratch("symlink-lock");
    std::fs::create_dir_all(root.join("real")).unwrap();
    std::os::unix::fs::symlink(root.join("real"), root.join("alias")).unwrap();
    let profile = root.join("alias/missing/profile");
    let before = lock_file_name(&profile);
    super::super::seed(&profile, false, false).unwrap();
    assert_eq!(before, lock_file_name(&profile));
    assert_eq!(before, lock_file_name(&root.join("real/missing/profile")));
    std::fs::remove_dir_all(root).unwrap();
}
