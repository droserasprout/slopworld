use super::*;

#[test]
fn local_uri_decodes_utf8_spaces_and_literal_percent_once() {
    assert_eq!(
        local_image_uri(" file:///tmp/a%20%CE%BB%2520.PNG\n").unwrap(),
        Some(PathBuf::from("/tmp/a λ%20.PNG"))
    );
    assert_eq!(
        local_image_uri("file://localhost/tmp/a.jpg").unwrap(),
        Some(PathBuf::from("/tmp/a.jpg"))
    );
    for text in [
        "hello",
        "file:///tmp/a.txt",
        "file://other/tmp/a.png",
        "file:///tmp/a.png\nfile:///tmp/b.png",
        "prefix file:///tmp/a.png",
    ] {
        assert_eq!(local_image_uri(text).unwrap(), None, "{text}");
    }
    for text in [
        "file:///tmp/a%zz.png",
        "file:///tmp/a%00.png",
        "file:///tmp/a.png?x=1",
        "file:///tmp/a.png#frag",
    ] {
        assert!(local_image_uri(text).is_err(), "{text}");
    }
}

fn temp() -> PathBuf {
    let root = std::env::temp_dir().join(format!("slopd-image-test-{}", uuid::Uuid::new_v4()));
    fs::create_dir(&root).unwrap();
    root
}

fn png() -> Vec<u8> {
    let mut bytes = Cursor::new(Vec::new());
    image::DynamicImage::new_rgb8(1, 1)
        .write_to(&mut bytes, image::ImageFormat::Png)
        .unwrap();
    bytes.into_inner()
}

#[test]
fn validates_actual_data_and_rejects_truncation_nonfiles_and_oversize() {
    let root = temp();
    let path = root.join("image.jpg");
    let bytes = png();
    fs::write(&path, &bytes).unwrap();
    let (read, extension) = image_bytes(&path).unwrap();
    assert_eq!(read, bytes);
    assert_eq!(extension, "png", "content selects the staged suffix");
    fs::write(&path, &bytes[..bytes.len() / 2]).unwrap();
    image_bytes(&path).unwrap_err();
    fs::write(&path, b"not an image").unwrap();
    image_bytes(&path).unwrap_err();
    OpenOptions::new()
        .write(true)
        .open(&path)
        .unwrap()
        .set_len(MAX_BYTES + 1)
        .unwrap();
    image_bytes(&path).unwrap_err();
    image_bytes(&root).unwrap_err();
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn staging_is_private_unique_and_rolls_back_until_committed() {
    use std::os::unix::fs::PermissionsExt;
    let root = temp();
    let store = root.join("images");
    let first = stage(&store, &png(), "png").unwrap();
    assert!(first.guest().starts_with(GUEST));
    assert_eq!(
        fs::metadata(&first.host).unwrap().permissions().mode() & 0o777,
        0o600
    );
    assert_eq!(
        fs::metadata(&store).unwrap().permissions().mode() & 0o777,
        0o700
    );
    let second = stage(&store, &png(), "png").unwrap();
    assert_ne!(first.guest(), second.guest());
    let abandoned = first.host.clone();
    drop(first);
    assert!(!abandoned.exists());
    let committed = second.host.clone();
    second.commit();
    assert!(committed.exists());
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn staging_rejects_symlink_redirection_and_full_storage() {
    use std::os::unix::fs::symlink;
    let root = temp();
    let alias = root.join("alias");
    symlink(&root, &alias).unwrap();
    stage(&alias.join("images"), &png(), "png").unwrap_err();
    assert!(!root.join("images").exists());
    let store = root.join("store");
    fs::create_dir(&store).unwrap();
    let large = store.join("large.png");
    OpenOptions::new()
        .write(true)
        .create_new(true)
        .open(&large)
        .unwrap()
        .set_len(MAX_STORED)
        .unwrap();
    stage(&store, &png(), "png").unwrap_err();
    fs::remove_file(large).unwrap();
    symlink(root.join("missing"), store.join("link")).unwrap();
    stage(&store, &png(), "png").unwrap_err();
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn host_alias_cannot_import_another_sessions_private_image() {
    let Some(_root) = crate::test_support::isolated() else {
        return;
    };
    let session = SessionCfg::default();
    prepare(&session).unwrap();
    let source = directory(&session).unwrap().join("private.png");
    fs::write(&source, png()).unwrap();
    let alias =
        std::env::temp_dir().join(format!("slopd-image-alias-{}.png", uuid::Uuid::new_v4()));
    std::os::unix::fs::symlink(&source, &alias).unwrap();
    let error = image_bytes(&alias).unwrap_err();
    assert!(error.to_string().contains("session private state"));
    fs::remove_file(alias).unwrap();
    crate::sandbox::remove_ephemeral_state(&session).unwrap();
}

#[tokio::test]
async fn canceled_blocking_import_removes_its_staged_file_when_the_job_finishes() {
    let root = temp();
    let worker_root = root.clone();
    let (ready, started) = tokio::sync::oneshot::channel();
    let (release, released) = std::sync::mpsc::channel();
    let job = tokio::task::spawn_blocking(move || {
        let imported = stage(&worker_root, &png(), "png").unwrap();
        ready.send(()).unwrap();
        released.recv().unwrap();
        imported
    });
    started.await.unwrap();
    assert_eq!(fs::read_dir(&root).unwrap().count(), 1);
    // spawn_blocking cannot stop a running read/decode job. Dropping its waiter
    // must still drop the uncommitted result when that job eventually returns.
    drop(job);
    release.send(()).unwrap();
    tokio::time::timeout(std::time::Duration::from_secs(2), async {
        while fs::read_dir(&root).unwrap().count() != 0 {
            tokio::time::sleep(std::time::Duration::from_millis(5)).await;
        }
    })
    .await
    .unwrap();
    fs::remove_dir_all(root).unwrap();
}
