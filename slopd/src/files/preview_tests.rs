use super::*;

use crate::test_support::fixture;
#[tokio::test]
async fn preview_reads_utf8_and_rejects_oversized_files() {
    let dir = fixture("preview");
    let path = dir.join("README.md");
    std::fs::write(&path, "# hello\n\nworld\n").unwrap();
    assert_eq!(read_text(&path, None).await.unwrap(), "# hello\n\nworld\n");

    let large = dir.join("large.md");
    std::fs::write(&large, vec![b'x'; usize::try_from(READ_LIMIT).unwrap() + 1]).unwrap();
    let error = read_text(&large, None).await.unwrap_err().to_string();
    assert!(error.contains("preview limit"));
}

#[tokio::test]
async fn image_reads_bytes_and_rejects_oversized_files() {
    let dir = fixture("image");
    let path = dir.join("work.png");
    let bytes = vec![137u8, 80, 78, 71];
    std::fs::write(&path, &bytes).unwrap();
    assert_eq!(read_image(&path, None).await.unwrap(), bytes);

    let large = dir.join("large.png");
    std::fs::write(&large, vec![0u8; usize::try_from(IMAGE_LIMIT).unwrap() + 1]).unwrap();
    let error = read_image(&large, None).await.unwrap_err().to_string();
    assert!(error.contains("image") && error.contains("limit"));
}

#[tokio::test]
async fn text_rejects_invalid_utf8_while_image_preserves_the_same_bytes() {
    let dir = fixture("preview-encoding");
    let path = dir.join("binary");
    std::fs::write(&path, [0xff, 0, 0xfe]).unwrap();
    read_text(&path, None).await.unwrap_err();
    assert_eq!(read_image(&path, None).await.unwrap(), [0xff, 0, 0xfe]);
}
