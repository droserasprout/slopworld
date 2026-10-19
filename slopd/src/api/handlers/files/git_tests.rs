use super::*;
use prost::Message;
use std::path::Path;

fn git(path: &Path, args: &[&str]) {
    let output = std::process::Command::new("git")
        .arg("-C")
        .arg(path)
        .args(args)
        .output()
        .unwrap();
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
}

#[tokio::test]
async fn non_repository_reply_keeps_requested_path() {
    let fixture = crate::api::handlers::tests::fixture("git-wire-none");
    let manager = crate::session::test_manager(crate::config::Config::default());
    let Proto(reply) = git_status(
        State(manager),
        Query(GitReq {
            path: fixture.to_str().unwrap().into(),
            counts: None,
        }),
    )
    .await
    .unwrap();
    let reply = wire::GitResult::decode(reply.encode_to_vec().as_slice()).unwrap();
    assert!(!reply.repo);
    assert_eq!(reply.path, fixture.to_str().unwrap());
    assert!(reply.files.is_empty());
    assert_eq!(reply.added, None);
    assert_eq!(reply.deleted, None);
}

#[tokio::test]
async fn status_wire_distinguishes_uncounted_binary_and_zero_line_counts() {
    let fixture = crate::api::handlers::tests::fixture("git-wire-counts");
    git(&fixture, &["init", "--initial-branch=main"]);
    git(&fixture, &["config", "user.name", "Test"]);
    git(&fixture, &["config", "user.email", "test@example.invalid"]);
    std::fs::write(fixture.join("text"), "original\n").unwrap();
    std::fs::write(fixture.join("binary"), b"old\0data").unwrap();
    git(&fixture, &["add", "."]);
    git(
        &fixture,
        &["-c", "commit.gpgsign=false", "commit", "-m", "initial"],
    );
    std::fs::write(fixture.join("text"), "original\nadded\n").unwrap();
    std::fs::write(fixture.join("binary"), b"new\0data").unwrap();
    let manager = crate::session::test_manager(crate::config::Config::default());
    for counts in [false, true] {
        let Proto(reply) = git_status(
            State(manager.clone()),
            Query(GitReq {
                path: fixture.to_str().unwrap().into(),
                counts: Some(counts),
            }),
        )
        .await
        .unwrap();
        let reply = wire::GitResult::decode(reply.encode_to_vec().as_slice()).unwrap();
        assert!(reply.repo);
        assert_eq!(reply.root, fixture.to_str().unwrap());
        assert_eq!(reply.branch, "main");
        assert_eq!(reply.changed, 2);
        assert_eq!(reply.counts_complete, counts);
        assert!(!reply.truncated);
        let text = reply.files.iter().find(|file| file.path == "text").unwrap();
        let binary = reply
            .files
            .iter()
            .find(|file| file.path == "binary")
            .unwrap();
        assert_eq!(text.status, " M");
        assert_eq!(text.added, counts.then_some(1));
        assert_eq!(text.deleted, counts.then_some(0));
        assert_eq!(binary.added, None);
        assert_eq!(binary.deleted, None);
        assert_eq!(reply.added, Some(if counts { 1 } else { 0 }));
        assert_eq!(reply.deleted, Some(0));
    }
}

#[tokio::test]
async fn rename_source_survives_status_wire_with_and_without_counts() {
    let fixture = crate::api::handlers::tests::fixture("git-wire-rename");
    git(&fixture, &["init", "--initial-branch=main"]);
    std::fs::write(fixture.join("old.txt"), "original\n").unwrap();
    git(&fixture, &["add", "."]);
    git(
        &fixture,
        &[
            "-c",
            "user.name=Test",
            "-c",
            "user.email=test@example.invalid",
            "-c",
            "commit.gpgsign=false",
            "commit",
            "-m",
            "initial",
        ],
    );
    git(&fixture, &["mv", "old.txt", "new.txt"]);
    let manager = crate::session::test_manager(crate::config::Config::default());
    for counts in [false, true] {
        let Proto(reply) = git_status(
            State(manager.clone()),
            Query(GitReq {
                path: fixture.to_str().unwrap().into(),
                counts: Some(counts),
            }),
        )
        .await
        .unwrap();
        let reply = wire::GitResult::decode(reply.encode_to_vec().as_slice()).unwrap();
        assert_eq!(reply.files.len(), 1);
        assert_eq!(reply.files[0].path, "new.txt");
        assert_eq!(reply.files[0].source.as_deref(), Some("old.txt"));
    }
}
