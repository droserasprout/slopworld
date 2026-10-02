use super::*;
use std::os::unix::fs::symlink;

#[test]
fn scoped_open_accepts_internal_links_and_rejects_escaping_links() {
    let fixture = super::super::tests::fixture("preview-scope");
    let root = fixture.join("project");
    std::fs::create_dir(&root).unwrap();
    std::fs::write(root.join("inside"), b"inside").unwrap();
    std::fs::write(fixture.join("outside"), b"outside").unwrap();
    symlink("inside", root.join("local-link")).unwrap();
    symlink("../outside", root.join("escape")).unwrap();
    symlink(&*fixture, root.join("escape-dir")).unwrap();
    open(&root, &root.join("inside")).unwrap();
    open(&root, &root.join("local-link")).unwrap();
    open(&root, &root.join("escape")).unwrap_err();
    open(&root, &root.join("escape-dir/outside")).unwrap_err();
    open(&root, &fixture.join("outside")).unwrap_err();
}

#[test]
fn resolved_walk_rejects_symlink_replacements_after_validation() {
    let fixture = super::super::tests::fixture("preview-scope-race");
    std::fs::create_dir(fixture.join("dir")).unwrap();
    std::fs::write(fixture.join("dir/file"), b"inside").unwrap();
    let retained_root = File::open(&*fixture).unwrap();
    std::fs::rename(fixture.join("dir"), fixture.join("original")).unwrap();
    symlink("original", fixture.join("dir")).unwrap();
    walk(retained_root, Path::new("dir/file"), false).unwrap_err();
    let retained_root = File::open(&*fixture).unwrap();
    symlink("original/file", fixture.join("file")).unwrap();
    walk(retained_root, Path::new("file"), false).unwrap_err();
}
