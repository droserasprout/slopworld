use super::*;
use std::os::unix::ffi::OsStringExt;

struct Repository(PathBuf);

impl Repository {
    fn new() -> Self {
        let repo =
            Self(std::env::temp_dir().join(format!("slopd-git-paths-{}", uuid::Uuid::new_v4())));
        std::fs::create_dir_all(&repo.0).unwrap();
        repo.git(&["init", "-q"]);
        repo
    }

    fn git(&self, args: &[&str]) {
        let output = std::process::Command::new("git")
            .arg("--literal-pathspecs")
            .arg("-C")
            .arg(&self.0)
            .args(args)
            .output()
            .unwrap();
        assert!(
            output.status.success(),
            "{}",
            String::from_utf8_lossy(&output.stderr)
        );
    }

    fn commit(&self) {
        self.git(&["add", "."]);
        self.git(&[
            "-c",
            "user.name=Test",
            "-c",
            "user.email=test@example.invalid",
            "-c",
            "commit.gpgsign=false",
            "commit",
            "-qm",
            "initial",
        ]);
    }
}

impl Drop for Repository {
    fn drop(&mut self) {
        drop(std::fs::remove_dir_all(&self.0));
    }
}

#[tokio::test]
async fn untracked_directories_are_literal_paths_with_complete_counts() {
    let repo = Repository::new();
    let names = [
        ":draft",
        ":(exclude)notes",
        "[draft]",
        "star*",
        "question?",
        "日本語",
    ];
    for name in names {
        std::fs::create_dir(repo.0.join(name)).unwrap();
        std::fs::write(repo.0.join(name).join("file.txt"), "hello\n").unwrap();
    }
    for counts in [false, true] {
        let answer = status_with_counts(&repo.0, counts).await.unwrap().unwrap();
        assert_eq!(answer.changed, names.len());
        assert!(!answer.truncated);
        assert_eq!(answer.counts_complete, counts);
        for name in names {
            let row = answer
                .changes
                .iter()
                .find(|row| row.path == format!("{name}/file.txt"))
                .unwrap();
            assert_eq!(row.status, "??");
            assert_eq!(row.added, counts.then_some(1));
        }
    }
}

#[tokio::test]
async fn tracked_counts_and_rename_sources_are_literal_paths() {
    let repo = Repository::new();
    std::fs::write(repo.0.join(":old"), "original\n").unwrap();
    repo.commit();
    repo.git(&["mv", ":old", ":(exclude)new"]);
    let renamed = status(&repo.0).await.unwrap().unwrap();
    assert_eq!(renamed.changes.len(), 1);
    assert_eq!(renamed.changes[0].source.as_deref(), Some(":old"));
    assert_eq!(renamed.changes[0].path, ":(exclude)new");
    assert_eq!(renamed.changes[0].added, Some(0));
    assert_eq!(renamed.changes[0].deleted, Some(0));
    assert!(renamed.counts_complete);
    std::fs::write(repo.0.join(":(exclude)new"), "original\nadded\n").unwrap();
    let edited = status(&repo.0).await.unwrap().unwrap();
    assert_eq!(edited.added, 1);
    assert_eq!(edited.deleted, 0);
    assert!(edited.counts_complete);
}

#[tokio::test]
async fn unsupported_filename_does_not_alias_a_valid_replacement_character() {
    let repo = Repository::new();
    let invalid = repo
        .0
        .join(std::ffi::OsString::from_vec(b"file-\xff.txt".to_vec()));
    std::fs::write(&invalid, "invalid\n").unwrap();
    std::fs::write(repo.0.join("file-�.txt"), "valid\n").unwrap();
    for counts in [false, true] {
        let error = status_with_counts(&repo.0, counts).await.err().unwrap();
        assert_eq!(error.kind(), std::io::ErrorKind::InvalidData);
        assert!(error.to_string().contains("not valid UTF-8"));
    }
    std::fs::remove_file(invalid).unwrap();
    let recovered = status(&repo.0).await.unwrap().unwrap();
    assert_eq!(recovered.changes.len(), 1);
    assert_eq!(recovered.changes[0].path, "file-�.txt");
    assert_eq!(recovered.added, 1);
}

#[tokio::test]
async fn unsupported_rename_source_is_rejected() {
    let mut bytes: &[u8] = b"R  valid.txt\0old-\xff.txt\0";
    let error = read_status_rows(&mut bytes).await.err().unwrap();
    assert_eq!(error.kind(), std::io::ErrorKind::InvalidData);
}

#[tokio::test]
async fn repository_root_preserves_carriage_return_and_rejects_invalid_utf8() {
    let repo = Repository::new();
    for name in [
        std::ffi::OsString::from("root\r"),
        std::ffi::OsString::from_vec(b"root-\xff".to_vec()),
    ] {
        let root = repo.0.join(&name);
        std::fs::create_dir(&root).unwrap();
        let output = std::process::Command::new("git")
            .arg("init")
            .arg("-q")
            .arg(&root)
            .output()
            .unwrap();
        assert!(output.status.success());
        let answer = toplevel(&root).await;
        if name.to_str().is_some() {
            assert_eq!(answer.unwrap(), Some(root));
        } else {
            assert_eq!(answer.unwrap_err().kind(), std::io::ErrorKind::InvalidData);
        }
    }
}
