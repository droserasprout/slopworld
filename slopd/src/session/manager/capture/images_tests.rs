use super::*;
use std::io::Cursor;

struct Fixture {
    manager: Arc<Manager>,
    session: SessionCfg,
    source: std::path::PathBuf,
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
        let source =
            std::env::temp_dir().join(format!("slopd-import-{}.png", uuid::Uuid::new_v4()));
        let mut bytes = Cursor::new(Vec::new());
        image::DynamicImage::new_rgb8(1, 1)
            .write_to(&mut bytes, image::ImageFormat::Png)
            .unwrap();
        std::fs::write(&source, bytes.into_inner()).unwrap();
        Self {
            manager,
            session,
            source,
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

    fn uri(&self) -> String {
        url::Url::from_file_path(&self.source).unwrap().into()
    }
}

impl Drop for Fixture {
    fn drop(&mut self) {
        std::fs::remove_file(&self.source).unwrap();
        crate::sandbox::remove_ephemeral_state(&self.session).unwrap();
    }
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
        .paste_host_file_image(&fixture.session.name, &fixture.uri(), Some(7))
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
    assert_eq!(
        std::fs::read(file).unwrap(),
        std::fs::read(&fixture.source).unwrap()
    );
    assert!(matches!(
        fixture.rx.try_recv().unwrap(),
        Input::Keys { literal: false, .. }
    ));
    let state = crate::sandbox::state_dir(&fixture.session).unwrap();
    crate::sandbox::remove_ephemeral_state(&fixture.session).unwrap();
    assert!(!state.exists());
}

#[tokio::test]
async fn missing_mount_and_invalid_data_never_paste_a_literal_uri_or_import_path() {
    let Some(_root) = crate::test_support::isolated() else {
        return;
    };
    let mut fixture = Fixture::new().await;
    let error = fixture
        .manager
        .paste_host_file_image(&fixture.session.name, &fixture.uri(), Some(7))
        .await
        .unwrap_err();
    assert!(error.to_string().contains("restart"));
    fixture.publish_mount().await;
    std::fs::write(&fixture.source, b"bad image").unwrap();
    fixture
        .manager
        .paste_host_file_image(&fixture.session.name, &fixture.uri(), Some(7))
        .await
        .unwrap_err();
    assert!(fixture.rx.try_recv().is_err());
    assert!(!images::directory(&fixture.session).unwrap().exists());
    fixture
        .manager
        .paste_host_file_image(&fixture.session.name, "normal text", Some(7))
        .await
        .unwrap();
    assert!(
        matches!(fixture.rx.try_recv().unwrap(), Input::Paste { bytes } if bytes == b"normal text")
    );
}

#[tokio::test]
async fn replaced_run_or_identity_removes_uncommitted_import_without_delivery() {
    let Some(_root) = crate::test_support::isolated() else {
        return;
    };
    let mut fixture = Fixture::new().await;
    for replace_identity in [false, true] {
        let imported = images::import(&fixture.session, &fixture.source).unwrap();
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
async fn mismatched_mount_identity_rejects_import_before_reading_host_file() {
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
        .paste_host_file_image(&fixture.session.name, &fixture.uri(), Some(7))
        .await
        .unwrap_err();
    assert!(fixture.rx.try_recv().is_err());
    assert!(!images::directory(&fixture.session).unwrap().exists());
}
