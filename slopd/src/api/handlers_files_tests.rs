use super::*;
use std::path::PathBuf;

struct Fixture(PathBuf);

impl Fixture {
    fn new() -> Self {
        let path =
            std::env::temp_dir().join(format!("slopd-file-mutations-{}", uuid::Uuid::new_v4()));
        std::fs::create_dir(&path).unwrap();
        Self(path)
    }
}

impl Drop for Fixture {
    fn drop(&mut self) {
        let _ = std::fs::remove_dir_all(&self.0);
    }
}

fn request(path: &Path, name: &str, kind: &str) -> Proto<wire::FileReq> {
    Proto(wire::FileReq {
        path: Some(path.to_str().unwrap().to_owned()),
        name: Some(name.to_owned()),
        kind: Some(kind.to_owned()),
    })
}

fn rejected(result: ApiResult<wire::Ack>, message: &str) {
    let (status, Proto(error)) = result.unwrap_err();
    assert_eq!(status, StatusCode::BAD_REQUEST);
    assert!(error.error.contains(message), "{}", error.error);
}

#[tokio::test]
async fn create_rename_and_remove_file_preserve_contents() {
    let fixture = Fixture::new();
    let parent = fixture.0.join("parent with spaces");
    std::fs::create_dir(&parent).unwrap();
    assert!(
        create_file(request(&parent, "  café.txt  ", " file "))
            .await
            .unwrap()
            .0
            .ok
    );
    let source = parent.join("café.txt");
    assert_eq!(std::fs::read(&source).unwrap(), b"");
    std::fs::write(&source, b"keep these contents\0\xff").unwrap();
    assert!(
        rename_file(request(&source, " renamed.txt ", ""))
            .await
            .unwrap()
            .0
            .ok
    );
    let target = parent.join("renamed.txt");
    assert!(!source.exists());
    assert_eq!(
        std::fs::read(&target).unwrap(),
        b"keep these contents\0\xff"
    );
    assert!(remove_file(request(&target, "", "")).await.unwrap().0.ok);
    assert!(parent.is_dir());
    assert_eq!(std::fs::read_dir(&parent).unwrap().count(), 0);
}

#[tokio::test]
async fn create_rename_and_remove_nonempty_folder() {
    let fixture = Fixture::new();
    assert!(
        create_file(request(&fixture.0, "folder", " folder "))
            .await
            .unwrap()
            .0
            .ok
    );
    let source = fixture.0.join("folder");
    std::fs::create_dir(source.join("nested")).unwrap();
    std::fs::write(source.join("nested/child"), "contents").unwrap();
    std::fs::write(fixture.0.join("sibling"), "untouched").unwrap();
    assert!(
        rename_file(request(&source, "renamed", ""))
            .await
            .unwrap()
            .0
            .ok
    );
    let target = fixture.0.join("renamed");
    assert!(!source.exists());
    assert_eq!(
        std::fs::read(target.join("nested/child")).unwrap(),
        b"contents"
    );
    assert!(remove_file(request(&target, "", "")).await.unwrap().0.ok);
    assert!(!target.exists());
    assert_eq!(
        std::fs::read(fixture.0.join("sibling")).unwrap(),
        b"untouched"
    );
}

#[tokio::test]
async fn duplicate_create_and_rename_leave_both_entries_intact() {
    let fixture = Fixture::new();
    let source = fixture.0.join("source");
    let target = fixture.0.join("target");
    std::fs::write(&source, "source data").unwrap();
    for directory in [false, true] {
        if directory {
            std::fs::create_dir(&target).unwrap();
            std::fs::write(target.join("child"), "target data").unwrap();
        } else {
            std::fs::write(&target, "target data").unwrap();
        }
        for kind in ["file", "folder"] {
            rejected(
                create_file(request(&fixture.0, "target", kind)).await,
                "already exists",
            );
        }
        rejected(
            rename_file(request(&source, "target", "")).await,
            "already exists",
        );
        rejected(
            rename_file(request(&source, "source", "")).await,
            "already exists",
        );
        assert_eq!(std::fs::read(&source).unwrap(), b"source data");
        let content_path = if directory {
            target.join("child")
        } else {
            target.clone()
        };
        assert_eq!(std::fs::read(content_path).unwrap(), b"target data");
        if !directory {
            std::fs::remove_file(&target).unwrap();
        }
    }
}

#[tokio::test]
async fn invalid_names_cannot_create_or_move_entries() {
    let fixture = Fixture::new();
    let source = fixture.0.join("source");
    std::fs::write(&source, "unchanged").unwrap();
    for name in [
        "",
        " \t ",
        ".",
        "..",
        "../escape",
        "child/file",
        "child\\file",
        "bad\0name",
    ] {
        rejected(create_file(request(&fixture.0, name, "file")).await, "name");
        rejected(rename_file(request(&source, name, "")).await, "name");
        assert_eq!(std::fs::read(&source).unwrap(), b"unchanged");
        assert_eq!(std::fs::read_dir(&fixture.0).unwrap().count(), 1);
    }
}

#[tokio::test]
async fn mutations_reject_empty_and_relative_paths() {
    for path in ["", " \t ", "relative", "./relative", "../relative"] {
        let expected = if path.trim().is_empty() {
            "no path"
        } else {
            "path must be absolute"
        };
        rejected(
            create_file(request(Path::new(path), "child", "file")).await,
            expected,
        );
        rejected(
            rename_file(request(Path::new(path), "child", "")).await,
            expected,
        );
        rejected(
            remove_file(request(Path::new(path), "", "")).await,
            expected,
        );
    }
}

#[tokio::test]
async fn create_rejects_missing_or_file_parents_and_unsupported_kinds() {
    let fixture = Fixture::new();
    let file = fixture.0.join("file");
    std::fs::write(&file, "unchanged").unwrap();
    rejected(
        create_file(request(&fixture.0.join("missing"), "child", "file")).await,
        "reading parent directory",
    );
    rejected(
        create_file(request(&file, "child", "file")).await,
        "not a directory",
    );
    for kind in ["", "directory", "symlink", "FILE"] {
        rejected(
            create_file(request(&fixture.0, "child", kind)).await,
            "kind must be file or folder",
        );
    }
    assert_eq!(std::fs::read(&file).unwrap(), b"unchanged");
    assert_eq!(std::fs::read_dir(&fixture.0).unwrap().count(), 1);
}

#[tokio::test]
async fn rename_and_remove_missing_paths_do_not_create_entries() {
    let fixture = Fixture::new();
    let missing = fixture.0.join("missing");
    rejected(
        rename_file(request(&missing, "target", "")).await,
        "reading",
    );
    rejected(remove_file(request(&missing, "", "")).await, "reading");
    assert_eq!(std::fs::read_dir(&fixture.0).unwrap().count(), 0);
}

#[cfg(unix)]
#[tokio::test]
async fn symlink_mutations_preserve_external_targets_including_dangling_links() {
    let fixture = Fixture::new();
    let external = Fixture::new();
    let file = external.0.join("file");
    let directory = external.0.join("directory");
    let missing = external.0.join("missing");
    std::fs::write(&file, "file data").unwrap();
    std::fs::create_dir(&directory).unwrap();
    std::fs::write(directory.join("child"), "child data").unwrap();
    let source = fixture.0.join("source");
    std::fs::write(&source, "source data").unwrap();
    for target in [&file, &directory, &missing] {
        let link = fixture.0.join("link");
        std::os::unix::fs::symlink(target, &link).unwrap();
        for kind in ["file", "folder"] {
            rejected(
                create_file(request(&fixture.0, "link", kind)).await,
                "already exists",
            );
        }
        rejected(
            rename_file(request(&source, "link", "")).await,
            "already exists",
        );
        assert_eq!(std::fs::read_link(&link).unwrap(), *target);
        assert!(
            rename_file(request(&link, "renamed", ""))
                .await
                .unwrap()
                .0
                .ok
        );
        assert!(std::fs::symlink_metadata(&link).is_err());
        let renamed = fixture.0.join("renamed");
        assert_eq!(std::fs::read_link(&renamed).unwrap(), *target);
        assert!(remove_file(request(&renamed, "", "")).await.unwrap().0.ok);
        assert!(std::fs::symlink_metadata(&renamed).is_err());
        assert_eq!(std::fs::read(&file).unwrap(), b"file data");
        assert_eq!(
            std::fs::read(directory.join("child")).unwrap(),
            b"child data"
        );
        assert!(!missing.exists());
        assert_eq!(std::fs::read(&source).unwrap(), b"source data");
    }
}

#[cfg(unix)]
#[tokio::test]
async fn recursive_remove_does_not_follow_nested_directory_symlinks() {
    let fixture = Fixture::new();
    let external = Fixture::new();
    std::fs::write(external.0.join("keep"), "external data").unwrap();
    let folder = fixture.0.join("folder");
    std::fs::create_dir(&folder).unwrap();
    std::os::unix::fs::symlink(&external.0, folder.join("link")).unwrap();
    assert!(remove_file(request(&folder, "", "")).await.unwrap().0.ok);
    assert!(!folder.exists());
    assert_eq!(
        std::fs::read(external.0.join("keep")).unwrap(),
        b"external data"
    );
}
