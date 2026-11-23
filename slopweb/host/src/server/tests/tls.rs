//! Exercise both upstream transports with a private, process-local trust anchor.
use super::*;
use std::{env, process::Command};
use tokio::net::TcpStream;
use tokio_rustls::{
    TlsAcceptor,
    rustls::{
        ServerConfig,
        pki_types::{CertificateDer, PrivateKeyDer, pem::PemObject},
    },
};

#[tokio::test]
async fn both_daemon_transports_honor_native_trust() {
    const TEST_ENDPOINT: &str = "SLOPWEB_TEST_TLS_ENDPOINT";
    if let Ok(endpoint) = env::var(TEST_ENDPOINT) {
        check_transports(&endpoint).await;
        return;
    }

    let files = tempfile::tempdir().unwrap();
    let cert = files.path().join("daemon.pem");
    let key = files.path().join("daemon.key");
    let output = Command::new("openssl")
        .args([
            "req",
            "-x509",
            "-newkey",
            "ec",
            "-pkeyopt",
            "ec_paramgen_curve:P-256",
            "-nodes",
            "-days",
            "1",
            "-subj",
            "/CN=localhost",
            "-addext",
            "subjectAltName=IP:127.0.0.1",
            "-addext",
            "basicConstraints=critical,CA:FALSE",
            "-out",
        ])
        .arg(&cert)
        .arg("-keyout")
        .arg(&key)
        .output()
        .unwrap();
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    let config = ServerConfig::builder()
        .with_no_client_auth()
        .with_single_cert(
            vec![CertificateDer::from_pem_file(&cert).unwrap()],
            PrivateKeyDer::from_pem_file(key).unwrap(),
        )
        .unwrap();
    let acceptor = TlsAcceptor::from(Arc::new(config));
    let listener = TcpListener::bind("127.0.0.1:0").await.unwrap();
    let endpoint = format!("https://{}", listener.local_addr().unwrap());
    let daemon = listen(
        Router::new()
            .route("/api/health", any(|| async { "daemon" }))
            .route(
                "/ws",
                any(|upgrade: axum::extract::ws::WebSocketUpgrade| async {
                    upgrade
                        .protocols(["slopworld.protobuf.v2"])
                        .on_upgrade(|_| async {})
                }),
            ),
    )
    .await;
    let address = daemon.url.strip_prefix("http://").unwrap().to_owned();
    let proxy = tokio::spawn(async move {
        loop {
            let (client, _) = listener.accept().await.unwrap();
            let acceptor = acceptor.clone();
            let address = address.clone();
            tokio::spawn(async move {
                let mut tls = acceptor.accept(client).await.unwrap();
                let mut upstream = TcpStream::connect(address).await.unwrap();
                let _transfer_result = tokio::io::copy_bidirectional(&mut tls, &mut upstream).await;
            });
        }
    });

    // Native trust is process-wide. Isolate the certificate from concurrent tests.
    let child = tokio::process::Command::new(env::current_exe().unwrap())
        .arg("--exact")
        .arg(std::thread::current().name().unwrap())
        .arg("--nocapture")
        .env(TEST_ENDPOINT, endpoint)
        .env("SSL_CERT_FILE", cert)
        .kill_on_drop(true)
        .output();
    let output = tokio::time::timeout(Duration::from_secs(15), child)
        .await
        .unwrap()
        .unwrap();
    proxy.abort();
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
}

async fn check_transports(endpoint: &str) {
    let host = host(direct(endpoint), None).await;
    let health = client()
        .get(format!("{}/host/health", host.url))
        .header("origin", &host.url)
        .send()
        .await
        .unwrap();
    assert_eq!(health.status(), StatusCode::OK);
    assert_eq!(health.text().await.unwrap(), "daemon");
    let (_, handshake) = tokio_tungstenite::connect_async(ws_request(&host, &host.url))
        .await
        .unwrap();
    assert_eq!(handshake.status(), StatusCode::SWITCHING_PROTOCOLS);
}
