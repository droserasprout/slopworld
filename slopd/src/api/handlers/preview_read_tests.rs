use super::*;
use nix::sys::stat::Mode;
use nix::unistd::mkfifo;
use std::os::unix::fs::symlink;

#[tokio::test]
async fn text_and_image_reads_enforce_optional_scope_and_descriptor_limits() {
    let fixture = super::super::tests::fixture("scoped-preview-read");
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
