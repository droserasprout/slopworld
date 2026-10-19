use super::*;
use nix::sys::stat::Mode;
use nix::unistd::mkfifo;
use std::os::unix::fs::symlink;

#[tokio::test]
async fn preview_handlers_preserve_literal_paths_and_scope() {
    let Some(root) = crate::test_support::isolated_with_env(|command, _| {
        command.env("SLOPD_PREVIEW_TEST_NAME", "other");
    }) else {
        return;
    };
    let manager = crate::session::test_manager_with_socket(
        crate::config::Config::default(),
        format!("preview-{}", uuid::Uuid::new_v4()),
    );
    let scope = root.join("$SLOPD_PREVIEW_TEST_NAME");
    std::fs::create_dir(&scope).unwrap();
    for name in ["file ", "$SLOPD_PREVIEW_TEST_NAME"] {
        let path = scope.join(name);
        std::fs::write(&path, "literal contents").unwrap();
        let other = scope.join(if name == "file " { "file" } else { "other" });
        std::fs::write(&other, "wrong contents").unwrap();
        for scoped in [false, true] {
            let request = || {
                Query(ReadReq {
                    path: path.to_str().unwrap().to_owned(),
                    root: scoped.then(|| scope.to_str().unwrap().to_owned()),
                })
            };
            let text = read_file(State(manager.clone()), request())
                .await
                .unwrap()
                .0;
            assert_eq!(text.path, path.to_str().unwrap());
            assert_eq!(text.text, "literal contents");
            let image = read_image(State(manager.clone()), request())
                .await
                .unwrap()
                .0;
            assert_eq!(image.path, path.to_str().unwrap());
            assert_eq!(image.data, b"literal contents");
            let stat = file_stat(State(manager.clone()), request())
                .await
                .unwrap()
                .0;
            assert_eq!(stat.stamp, reader_stat(&path).await.unwrap().stamp);
        }
    }
}

#[tokio::test]
async fn text_and_image_reads_enforce_optional_scope_and_descriptor_limits() {
    let fixture = crate::api::handlers::tests::fixture("scoped-preview-read");
    let root = fixture.join("project");
    std::fs::create_dir(&root).unwrap();
    std::fs::write(root.join("file"), b"data").unwrap();
    std::fs::write(fixture.join("outside"), b"secret").unwrap();
    symlink("../outside", root.join("escape")).unwrap();
    mkfifo(&root.join("pipe"), Mode::S_IRUSR | Mode::S_IWUSR).unwrap();
    for label in ["file", "image"] {
        assert_eq!(
            read_file_bounded(&root.join("file"), 4, label, root.to_str())
                .await
                .unwrap(),
            b"data"
        );
        read_file_bounded(&root.join("file"), 3, label, root.to_str())
            .await
            .unwrap_err();
        read_file_bounded(&root.join("escape"), 100, label, root.to_str())
            .await
            .unwrap_err();
        read_file_bounded(&root.join("file"), 100, label, Some(""))
            .await
            .unwrap_err();
        assert_eq!(
            read_file_bounded(&root.join("escape"), 100, label, None)
                .await
                .unwrap(),
            b"secret"
        );
        for scope in [None, root.to_str()] {
            read_file_bounded(&root.join("pipe"), 100, label, scope)
                .await
                .unwrap_err();
        }
    }
}
