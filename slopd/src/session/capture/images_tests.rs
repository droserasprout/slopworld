use super::*;
use std::io::Cursor;

struct Fixture {
    manager: Arc<SessionRuntime>,
    session: SessionCfg,
    bytes: Vec<u8>,
    rx: mpsc::UnboundedReceiver<Input>,
    _socket: crate::test_support::TmuxSocket,
}

impl Fixture {
    async fn new() -> Self {
        let socket = crate::test_support::TmuxSocket::new();
        let manager =
            crate::session::test_manager_with_socket(Config::default(), socket.path.clone());
        manager
            .tmux
            .spawn(
                "image-target",
                "/tmp",
                80,
                24,
                &["sleep".into(), "60".into()],
                false,
            )
            .await
            .unwrap();
        let session = SessionCfg {
            name: "image-target".into(),
            command: "codex".into(),
            ..Default::default()
        };
        let mut live = Live::new(session.clone(), TitleCapture::default());
        live.state = State::Working;
        live.run_id = 7;
        let (tx, rx) = mpsc::unbounded_channel();
        live.input.sender = Some(tx);
        manager
            .live
            .write()
            .await
            .insert(session.name.clone(), live);
        let mut bytes = Cursor::new(Vec::new());
        image::DynamicImage::new_rgb8(1, 1)
            .write_to(&mut bytes, image::ImageFormat::Png)
            .unwrap();
        Self {
            manager,
            session,
            bytes: bytes.into_inner(),
            rx,
            _socket: socket,
        }
    }

    async fn publish_mount(&self) {
        self.manager
            .tmux
            .set_option(
                &self.session.name,
                images::MOUNT_STATE,
                &self.session.state_id,
            )
            .await
            .unwrap();
    }
}

impl Drop for Fixture {
    fn drop(&mut self) {
        crate::sandbox::remove_ephemeral_state(&self.session).unwrap();
    }
}

#[tokio::test]
async fn uploaded_image_requires_current_run_and_mount_then_pastes_staged_bytes() {
    let Some(_root) = crate::test_support::isolated() else {
        return;
    };
    let mut fixture = Fixture::new().await;
    let bytes = fixture.bytes.clone();
    let manager = &fixture.manager;
    let name = &fixture.session.name;
    assert!(
        manager
            .paste_uploaded_image(name, bytes.clone(), Some(7))
            .await
            .unwrap_err()
            .to_string()
            .contains("restart")
    );
    fixture.publish_mount().await;
    for run in [None, Some(6)] {
        manager
            .paste_uploaded_image(name, bytes.clone(), run)
            .await
            .unwrap_err();
    }
    manager
        .paste_uploaded_image(name, b"not an image".to_vec(), Some(7))
        .await
        .unwrap_err();
    assert!(fixture.rx.try_recv().is_err());
    assert!(!images::directory(&fixture.session).unwrap().exists());
    manager
        .paste_uploaded_image(name, bytes.clone(), Some(7))
        .await
        .unwrap();
    let Input::Paste { bytes: path } = fixture.rx.try_recv().unwrap() else {
        panic!("expected image path")
    };
    let path = String::from_utf8(path).unwrap();
    assert!(path.starts_with(images::GUEST));
    let file = images::directory(&fixture.session)
        .unwrap()
        .join(std::path::Path::new(&path).file_name().unwrap());
    assert_eq!(std::fs::read(file).unwrap(), bytes);
    assert!(fixture.rx.try_recv().is_err());
}

#[tokio::test]
async fn imported_image_pastes_guest_path_in_order_and_follows_private_state_cleanup() {
    let Some(_root) = crate::test_support::isolated() else {
        return;
    };
    let mut fixture = Fixture::new().await;
    fixture.publish_mount().await;
    fixture
        .manager
        .send_keys(&fixture.session.name, vec!["prefix".into()], true)
        .await;
    fixture
        .manager
        .paste_uploaded_image(&fixture.session.name, fixture.bytes.clone(), Some(7))
        .await
        .unwrap();
    fixture
        .manager
        .send_keys(&fixture.session.name, vec!["Enter".into()], false)
        .await;
    assert!(matches!(
        fixture.rx.try_recv().unwrap(),
        Input::Keys { literal: true, .. }
    ));
    let Input::Paste { bytes } = fixture.rx.try_recv().unwrap() else {
        panic!("expected image path paste");
    };
    let guest = String::from_utf8(bytes).unwrap();
    assert!(guest.starts_with(images::GUEST));
    let file = images::directory(&fixture.session)
        .unwrap()
        .join(std::path::Path::new(&guest).file_name().unwrap());
    assert_eq!(std::fs::read(file).unwrap(), fixture.bytes.clone());
    assert!(matches!(
        fixture.rx.try_recv().unwrap(),
        Input::Keys { literal: false, .. }
    ));
    let state = crate::sandbox::state_dir(&fixture.session).unwrap();
    crate::sandbox::remove_ephemeral_state(&fixture.session).unwrap();
    assert!(!state.exists());
}

#[tokio::test]
async fn replaced_run_or_identity_removes_uncommitted_import_without_delivery() {
    let Some(_root) = crate::test_support::isolated() else {
        return;
    };
    let mut fixture = Fixture::new().await;
    for replace_identity in [false, true] {
        let imported = images::import_uploaded(&fixture.session, &fixture.bytes).unwrap();
        {
            let mut rows = fixture.manager.live.write().await;
            let row = rows.get_mut(&fixture.session.name).unwrap();
            if replace_identity {
                row.cfg.state_id = crate::storage_id::draft_identity();
            } else {
                row.run_id = 8;
            }
        }
        fixture
            .manager
            .commit_image_paste(
                &fixture.session.name,
                &fixture.session.state_id,
                7,
                imported,
            )
            .await
            .unwrap_err();
        assert_eq!(
            std::fs::read_dir(images::directory(&fixture.session).unwrap())
                .unwrap()
                .count(),
            0
        );
        assert!(fixture.rx.try_recv().is_err());
    }
}

#[tokio::test]
async fn mismatched_mount_identity_rejects_upload_before_staging() {
    let Some(_root) = crate::test_support::isolated() else {
        return;
    };
    let mut fixture = Fixture::new().await;
    fixture
        .manager
        .tmux
        .set_option(
            &fixture.session.name,
            images::MOUNT_STATE,
            "0000000000000000",
        )
        .await
        .unwrap();
    fixture
        .manager
        .paste_uploaded_image(&fixture.session.name, fixture.bytes.clone(), Some(7))
        .await
        .unwrap_err();
    assert!(fixture.rx.try_recv().is_err());
    assert!(!images::directory(&fixture.session).unwrap().exists());
}
