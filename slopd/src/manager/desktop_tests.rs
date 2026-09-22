use std::path::Path;

use super::{
    associated_apps, collect_desktop_files, content_type, desktop_apps, desktop_file_id,
    desktop_file_path_in, parse_desktop_entry, unescape_desktop_value,
};

#[test]
fn content_type_reads_gio_info() {
    assert_eq!(
        content_type("name: /tmp/a\n  standard::content-type: text/plain\n").unwrap(),
        "text/plain"
    );
}

#[test]
fn associations_keep_default_order_and_remove_duplicates() {
    let apps = desktop_apps(
            "Default application for “text/plain”: editor.desktop\n\nRegistered applications:\n\teditor.desktop\n\tviewer.desktop\nRecommended applications:\n\tviewer.desktop\n",
        );
    assert_eq!(
        apps.iter().map(|app| app.id.as_str()).collect::<Vec<_>>(),
        vec!["editor.desktop", "viewer.desktop"]
    );
}

#[test]
fn desktop_values_unescape_standard_sequences() {
    assert_eq!(unescape_desktop_value(r"A\sB\nC\\D"), "A B\nC\\D");
}

#[test]
fn text_types_include_plain_text_handlers() {
    let dir = tempfile_path("desktop-entry");
    std::fs::create_dir_all(&dir).unwrap();
    let path = dir.join("editor.desktop");
    std::fs::write(
        &path,
        "[Desktop Entry]\nType=Application\nName=Editor\nMimeType=text/plain;\n",
    )
    .unwrap();
    let entry = parse_desktop_entry("editor.desktop", &path).unwrap();
    assert!(entry.supports("text/x-toml"));
    assert!(!entry.supports("image/png"));
    let _ = std::fs::remove_file(path);
    let _ = std::fs::remove_dir(dir);
}

#[test]
fn full_associations_keep_gio_order_before_extra_entries() {
    let apps = associated_apps(
            "text/x-toml",
            "Default application for “text/x-toml”: first.desktop\nRegistered applications:\n\tfirst.desktop\n",
        );
    assert_eq!(
        apps.first().map(|app| app.id.as_str()),
        Some("first.desktop")
    );
}

#[test]
fn desktop_ids_resolve_to_launchable_files() {
    let root = tempfile_path("desktop-location");
    let applications = root.join("applications");
    std::fs::create_dir_all(&applications).unwrap();
    let path = applications.join("nested/editor.desktop");
    std::fs::create_dir_all(path.parent().unwrap()).unwrap();
    std::fs::write(&path, "[Desktop Entry]\nType=Application\nName=Editor\n").unwrap();

    assert_eq!(
        desktop_file_path_in("nested-editor.desktop", std::slice::from_ref(&root)),
        Some(path.clone())
    );
    assert_eq!(
        desktop_file_id(Path::new("nested/editor.desktop")),
        "nested-editor.desktop"
    );
    assert!(desktop_file_path_in("../editor.desktop", std::slice::from_ref(&root)).is_none());

    let _ = std::fs::remove_dir_all(root);
}

#[cfg(unix)]
#[test]
fn desktop_scan_does_not_follow_directory_symlinks() {
    use std::os::unix::fs::symlink;

    let root = tempfile_path("desktop-symlink-loop");
    let applications = root.join("applications");
    std::fs::create_dir_all(&applications).unwrap();
    let desktop = applications.join("editor.desktop");
    std::fs::write(&desktop, "[Desktop Entry]\nType=Application\nName=Editor\n").unwrap();
    symlink(&applications, applications.join("loop")).unwrap();

    let mut files = Vec::new();
    collect_desktop_files(&applications, &mut files);
    assert_eq!(files, vec![desktop]);

    let _ = std::fs::remove_dir_all(root);
}

fn tempfile_path(name: &str) -> std::path::PathBuf {
    std::env::temp_dir().join(format!("slopworld-{name}-{}", std::process::id()))
}
