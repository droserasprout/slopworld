use super::probe::wire;
use super::store::save_private;
use super::*;
use prost::Message;
use std::io::{Read, Write};
use std::net::TcpListener;
use std::sync::atomic::{AtomicUsize, Ordering};
use std::time::Duration;

fn root() -> std::path::PathBuf {
    super::super::tests::scratch(&format!("peers-{}", uuid::Uuid::new_v4()))
}

fn health() -> Vec<u8> {
    wire::Health {
        ok: true,
        protocol_version: 2,
        ..Default::default()
    }
    .encode_to_vec()
}

fn server(replies: Vec<(u16, &'static str, Vec<u8>)>) -> (String, std::thread::JoinHandle<()>) {
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let url = format!("http://{}", listener.local_addr().unwrap());
    let thread = std::thread::spawn(move || {
        for (status, token, body) in replies {
            let (mut stream, _) = listener.accept().unwrap();
            stream
                .set_read_timeout(Some(Duration::from_secs(5)))
                .unwrap();
            let mut request = Vec::new();
            loop {
                let mut byte = [0];
                stream.read_exact(&mut byte).unwrap();
                request.push(byte[0]);
                if request.ends_with(b"\r\n\r\n") {
                    break;
                }
            }
            let request = String::from_utf8(request).unwrap().to_lowercase();
            assert!(request.starts_with("get /api/health http/1.1\r\n"));
            assert!(request.contains("x-slop-session: host\r\n"));
            if token.is_empty() {
                assert!(!request.contains("saved-secret"));
            } else {
                assert!(request.contains(&format!("x-slop-token: {token}\r\n")));
            }
            write!(stream, "HTTP/1.1 {status} Test\r\ncontent-type: application/x-protobuf\r\ncontent-length: {}\r\nconnection: close\r\n\r\n", body.len()).unwrap();
            stream.write_all(&body).unwrap();
        }
    });
    (url, thread)
}

#[test]
fn local_registration_preserves_daemon_and_selection() {
    let root = root();
    let url = "http://remote.example:7717";
    for address in [None, Some(url)] {
        assert!(
            connect(&root, "local", address, || panic!("no prompt"))
                .unwrap_err()
                .contains("owned by the daemon")
        );
    }
    assert!(!root.exists());
    let path = root.join("endpoints/local.toml");
    let descriptor = "url = 'http://localhost:7717'\ntoken = 'saved-secret'\n";
    let selection = "endpoint = 'tank'\n";
    save_private(&path, descriptor).unwrap();
    save_private(&root.join("client.toml"), selection).unwrap();
    for address in [None, Some(url)] {
        assert!(connect(&root, "local", address, || panic!("no prompt")).is_err());
    }
    assert!(
        store::save(
            &root,
            "local",
            &Peer {
                url: url.into(),
                token: "replacement".into()
            }
        )
        .is_err()
    );
    assert_eq!(fs::read_to_string(path).unwrap(), descriptor);
    assert_eq!(
        fs::read_to_string(root.join("client.toml")).unwrap(),
        selection
    );
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn authenticated_connect_saves_private_peer_without_selection_and_reuses_credentials() {
    let root = root();
    let (url, server) = server(vec![
        (401, "", vec![]),
        (200, "saved-secret", health()),
        (200, "saved-secret", health()),
    ]);
    let prompts = AtomicUsize::new(0);
    connect(&root, "tank", Some(&url), || {
        prompts.fetch_add(1, Ordering::SeqCst);
        Ok("saved-secret".into())
    })
    .unwrap();
    assert_eq!(prompts.load(Ordering::SeqCst), 1);
    assert!(!root.join("client.toml").exists());
    let path = root.join("endpoints/tank.toml");
    assert_eq!(load(&path).unwrap().token, "saved-secret");
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        assert_eq!(
            fs::metadata(&path).unwrap().permissions().mode() & 0o777,
            0o600
        );
    }
    // Replacement repairs overly broad permissions as well as preserving credentials.
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        fs::set_permissions(&path, fs::Permissions::from_mode(0o644)).unwrap();
    }
    connect(&root, "tank", None, || panic!("saved key must be reused")).unwrap();
    #[cfg(unix)]
    {
        use std::os::unix::fs::PermissionsExt;
        assert_eq!(
            fs::metadata(&path).unwrap().permissions().mode() & 0o777,
            0o600
        );
    }
    server.join().unwrap();
    let listing = list(&root).unwrap();
    assert_eq!(listing, format!("tank\t{url}\n"));
    assert!(!listing.contains("saved-secret"));
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn rejected_key_keeps_existing_files_and_changed_urls_do_not_receive_saved_keys() {
    let root = root();
    let path = root.join("endpoints/tank.toml");
    save_private(
        &path,
        "url = 'http://old.example:7717'\ntoken = 'saved-secret'\n",
    )
    .unwrap();
    save_private(
        &root.join("client.toml"),
        "endpoint = 'local'\nother = 42\n",
    )
    .unwrap();
    let descriptor = fs::read(&path).unwrap();
    let selection = fs::read(root.join("client.toml")).unwrap();
    let (url, server) = server(vec![(401, "", vec![]), (401, "wrong-key", vec![])]);
    let error = connect(&root, "tank", Some(&url), || Ok("wrong-key".into())).unwrap_err();
    server.join().unwrap();
    assert!(error.contains("authentication failed"));
    assert!(!error.contains("wrong-key"));
    assert_eq!(fs::read(path).unwrap(), descriptor);
    assert_eq!(fs::read(root.join("client.toml")).unwrap(), selection);
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn invalid_protocol_and_redirects_do_not_prompt_or_write_files() {
    for (status, body) in [
        (200, vec![0x80]),
        (
            200,
            wire::Health {
                ok: true,
                protocol_version: 1,
                ..Default::default()
            }
            .encode_to_vec(),
        ),
        (302, vec![]),
    ] {
        let root = root();
        let (url, server) = server(vec![(status, "", body)]);
        assert!(
            connect(&root, "tank", Some(&url), || panic!(
                "protocol errors must not prompt"
            ))
            .is_err()
        );
        server.join().unwrap();
        assert!(!root.exists());
    }
}

#[test]
fn cancelled_key_entry_does_not_save_and_registration_ignores_client_settings() {
    let root = root();
    let (url, server) = server(vec![(401, "", vec![])]);
    assert!(connect(&root, "tank", Some(&url), || Err("cancelled".into())).is_err());
    server.join().unwrap();
    assert!(!root.exists());
    fs::create_dir_all(&root).unwrap();
    fs::write(root.join("client.toml"), "broken = [").unwrap();
    let (url, server) = self::server(vec![(200, "", health())]);
    connect(&root, "tank", Some(&url), || panic!("no prompt")).unwrap();
    server.join().unwrap();
    assert!(root.join("endpoints/tank.toml").is_file());
    assert_eq!(
        fs::read_to_string(root.join("client.toml")).unwrap(),
        "broken = ["
    );
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn registration_preserves_client_settings_and_empty_lists_create_nothing() {
    let root = root();
    assert!(list(&root).unwrap().contains("No saved endpoints"));
    assert!(!root.exists());
    save_private(
        &root.join("client.toml"),
        "endpoint = 'local'\nother = 42\n",
    )
    .unwrap();
    let (url, server) = server(vec![(200, "", health())]);
    connect(&root, "tank", Some(&url), || panic!("no prompt")).unwrap();
    server.join().unwrap();
    let settings: toml::Table =
        toml::from_str(&fs::read_to_string(root.join("client.toml")).unwrap()).unwrap();
    assert_eq!(settings["other"].as_integer(), Some(42));
    assert_eq!(settings["endpoint"].as_str(), Some("local"));
    fs::remove_dir_all(root).unwrap();
}

#[test]
fn peer_urls_match_the_mod_transport_and_never_embed_credentials() {
    assert_eq!(
        normalize_url("http://example.test:7717/").unwrap(),
        "http://example.test:7717"
    );
    assert_eq!(
        normalize_url("http://[::1]:7717").unwrap(),
        "http://[::1]:7717"
    );
    for (input, expected) in [
        ("https://example.test", "https://example.test"),
        ("https://example.test:443/", "https://example.test"),
        ("https://example.test:9443/", "https://example.test:9443"),
        ("https://[::1]:9443/", "https://[::1]:9443"),
    ] {
        assert_eq!(normalize_url(input).unwrap(), expected);
    }
    for url in [
        "ftp://example.test",
        "https://user:secret@example.test",
        "https://example.test/path",
        "https://example.test/?key=secret",
        "https://example.test/#fragment",
        "http://user:key@example.test",
        "http://example.test/path",
        "http://example.test/?key=secret",
        "http://example.test/#fragment",
        "invalid",
    ] {
        let error = normalize_url(url).unwrap_err();
        assert!(!error.contains("secret"));
    }
}
