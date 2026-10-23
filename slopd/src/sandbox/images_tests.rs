use super::*;

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
fn uploaded_bytes_validate_size_format_and_decode_before_staging() {
    assert_eq!(validate_image(&png()).unwrap(), "png");
    validate_image(b"not an image").unwrap_err();
    let bytes = png();
    validate_image(&bytes[..bytes.len() / 2]).unwrap_err();
    validate_image(&vec![0; usize::try_from(MAX_BYTES).unwrap() + 1]).unwrap_err();
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
