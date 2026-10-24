//! Exercise client dispatch, local defaults shared with slopctl, and offline profile setup.
#![cfg(test)]
use std::fs;
use std::process::Command;

struct Client(std::path::PathBuf);
impl Client {
    fn new() -> Self {
        Self(std::env::temp_dir().join(format!("slopworld-cli-{}", uuid::Uuid::new_v4())))
    }
    fn command(&self, executable: &str) -> Command {
        let mut command = Command::new(executable);
        command
            .env("SLOPD_CONFIG_ROOT", self.0.join("config"))
            .env("SLOPD_DATA", self.0.join("data"))
            .env("SLOPD_CACHE", self.0.join("cache"))
            .env("XDG_RUNTIME_DIR", self.0.join("runtime"))
            .env_remove("SLOPD_ENDPOINT")
            .env_remove("SLOPD_URL")
            .env_remove("SLOPD_TOKEN")
            .env_remove("SLOPWORLD_PROFILE");
        command
    }
    fn launcher(&self, args: &[&str]) -> String {
        self.output(env!("CARGO_BIN_EXE_slopworld"), args)
    }
    fn output(&self, executable: &str, args: &[&str]) -> String {
        let output = self.command(executable).args(args).output().unwrap();
        assert!(
            output.status.success(),
            "{}",
            String::from_utf8_lossy(&output.stderr)
        );
        String::from_utf8(output.stdout).unwrap()
    }
}
impl Drop for Client {
    fn drop(&mut self) {
        drop(fs::remove_dir_all(&self.0));
    }
}

#[test]
fn peer_dispatch_and_path_queries_do_not_create_state() {
    let client = Client::new();
    assert!(client.launcher(&["connect", "--help"]).contains("<NAME>"));
    assert!(
        client
            .launcher(&["endpoints"])
            .contains("No saved endpoints")
    );
    assert_eq!(
        client
            .launcher(&["--endpoint", "tank", "paths", "profile"])
            .trim(),
        client.0.join("data/profiles/tank").to_str().unwrap()
    );
    assert!(!client.0.exists());
    for args in [
        vec!["connect"],
        vec!["endpoints", "extra"],
        vec!["--endpoint", "tank", "connect", "other"],
    ] {
        assert!(
            !client
                .command(env!("CARGO_BIN_EXE_slopworld"))
                .args(args)
                .output()
                .unwrap()
                .status
                .success()
        );
    }
    assert!(!client.0.exists());
}

#[test]
fn clients_default_to_local_while_named_and_sidecar_overrides_remain_independent() {
    let client = Client::new();
    let config = client.0.join("config");
    fs::create_dir_all(config.join("endpoints")).unwrap();
    fs::write(
        config.join("endpoints/tank.toml"),
        "url = 'http://tank:7717'\ntoken = 'secret'\n",
    )
    .unwrap();
    fs::write(config.join("client.toml"), "endpoint = 'tank'\n").unwrap();
    let listing = client.launcher(&["endpoints"]);
    assert_eq!(listing, "tank\thttp://tank:7717\n");
    assert!(!listing.contains("secret"));
    let endpoint = client.launcher(&["paths", "endpoint"]);
    assert_eq!(
        endpoint.trim(),
        config.join("endpoints/local.toml").to_str().unwrap()
    );
    assert_eq!(
        client.output(env!("CARGO_BIN_EXE_slopctl"), &["paths", "endpoint"]),
        endpoint
    );
    assert_eq!(
        client
            .launcher(&["--endpoint=local", "paths", "profile"])
            .trim(),
        client.0.join("data/profiles/local").to_str().unwrap()
    );
    assert_eq!(
        client.launcher(&["--sidecar", "paths", "profile"]).trim(),
        client
            .0
            .join("data/sidecar/profiles/local")
            .to_str()
            .unwrap()
    );
    client.launcher(&["profile", "init"]);
    let profile = client.0.join("data/profiles/local");
    assert!(profile.join("slopworld.profile").exists());
    fs::write(profile.join("Config/ModsConfig.xml"), "custom mod list").unwrap();
    client.launcher(&["profile", "init"]);
    assert_eq!(
        fs::read_to_string(profile.join("Config/ModsConfig.xml")).unwrap(),
        "custom mod list"
    );
    assert_eq!(
        fs::read_to_string(config.join("client.toml")).unwrap(),
        "endpoint = 'tank'\n"
    );
}

#[expect(
    dead_code,
    reason = "test fixtures use the shared generated health message"
)]
mod wire {
    include!(concat!(env!("OUT_DIR"), "/slopworld.rs"));
}

#[test]
fn connect_registers_without_selecting_or_exposing_its_key() {
    use prost::Message;
    use std::io::{Read, Write};
    use std::net::TcpListener;
    use std::process::Stdio;
    use std::time::Duration;
    let client = Client::new();
    let listener = TcpListener::bind("127.0.0.1:0").unwrap();
    let url = format!("http://{}", listener.local_addr().unwrap());
    let server = std::thread::spawn(move || {
        for authenticated in [false, true] {
            let (mut stream, _) = listener.accept().unwrap();
            stream
                .set_read_timeout(Some(Duration::from_secs(5)))
                .unwrap();
            let mut request = Vec::new();
            while !request.ends_with(b"\r\n\r\n") {
                let mut byte = [0];
                stream.read_exact(&mut byte).unwrap();
                request.push(byte[0]);
            }
            let request = String::from_utf8(request).unwrap();
            assert_eq!(request.contains("cli-private-key"), authenticated);
            let body = if authenticated {
                wire::Health {
                    ok: true,
                    protocol_version: 2,
                    ..Default::default()
                }
                .encode_to_vec()
            } else {
                vec![]
            };
            let status = if authenticated { 200 } else { 401 };
            write!(stream, "HTTP/1.1 {status} Test\r\ncontent-type: application/x-protobuf\r\ncontent-length: {}\r\nconnection: close\r\n\r\n", body.len()).unwrap();
            stream.write_all(&body).unwrap();
        }
    });
    let mut child = client
        .command(env!("CARGO_BIN_EXE_slopworld"))
        .args(["connect", "tank", &url])
        .stdin(Stdio::piped())
        .stdout(Stdio::piped())
        .stderr(Stdio::piped())
        .spawn()
        .unwrap();
    child
        .stdin
        .take()
        .unwrap()
        .write_all(b"cli-private-key\n")
        .unwrap();
    let output = child.wait_with_output().unwrap();
    server.join().unwrap();
    assert!(
        output.status.success(),
        "{}",
        String::from_utf8_lossy(&output.stderr)
    );
    assert!(String::from_utf8_lossy(&output.stdout).contains("Saved peer tank"));
    assert!(String::from_utf8_lossy(&output.stderr).contains("Peer key:"));
    assert!(!String::from_utf8_lossy(&output.stderr).contains("cli-private-key"));
    assert!(!String::from_utf8_lossy(&output.stdout).contains("cli-private-key"));
    assert_eq!(client.launcher(&["endpoints"]), format!("tank\t{url}\n"));
    assert!(!client.0.join("config/client.toml").exists());
    assert!(
        client
            .launcher(&["paths", "endpoint"])
            .trim()
            .ends_with("endpoints/local.toml")
    );
    assert_eq!(
        client
            .output(
                env!("CARGO_BIN_EXE_slopctl"),
                &["--endpoint", "tank", "paths", "endpoint"]
            )
            .trim(),
        client
            .0
            .join("config/endpoints/tank.toml")
            .to_str()
            .unwrap()
    );
    client.launcher(&["--endpoint", "tank", "profile", "init"]);
    assert!(
        client
            .0
            .join("data/profiles/tank/slopworld.profile")
            .is_file()
    );
}

#[test]
fn completions_are_offline_and_match_packaged_scripts() {
    let client = Client::new();
    for (name, executable) in [
        ("slopctl", env!("CARGO_BIN_EXE_slopctl")),
        ("slopworld", env!("CARGO_BIN_EXE_slopworld")),
    ] {
        for (shell, directory, filename) in [
            ("bash", "bash-completion/completions", name.to_string()),
            ("zsh", "zsh/site-functions", format!("_{name}")),
            ("fish", "fish/vendor_completions.d", format!("{name}.fish")),
        ] {
            let generated = client.output(executable, &["completions", shell]);
            let packaged = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
                .join("../packaging/completions")
                .join(directory)
                .join(filename);
            assert_eq!(generated, fs::read_to_string(packaged).unwrap());
        }
    }
    assert!(!client.0.exists());
}

#[test]
fn profile_commands_work_offline_and_reset_only_the_mod_list() {
    for sidecar in [false, true] {
        let client = Client::new();
        let profile = client.0.join("custom profile");
        let run = |action| {
            let mut command = client.command(env!("CARGO_BIN_EXE_slopworld"));
            command.env("SLOPWORLD_GAME", client.0.join("missing-game"));
            if sidecar {
                command.arg("--sidecar");
            }
            let output = command
                .args(["profile", action, "--endpoint", "offline", "--profile"])
                .arg(&profile)
                .output()
                .unwrap();
            assert!(
                output.status.success(),
                "{}",
                String::from_utf8_lossy(&output.stderr)
            );
        };
        run("init");
        let mods = profile.join("Config/ModsConfig.xml");
        let initial_mods = fs::read(&mods).unwrap();
        let prefs = profile.join("Config/Prefs.xml");
        let settings = profile.join("Config/SlopWorld.toml");
        assert_eq!(settings.exists(), sidecar);
        let save = profile.join("Saves/colony.rws");
        fs::create_dir_all(save.parent().unwrap()).unwrap();
        for path in [&mods, &prefs, &settings, &save] {
            fs::write(path, "custom contents").unwrap();
        }
        fs::remove_file(profile.join("slopworld.profile")).unwrap();
        run("init");
        assert!(profile.join("slopworld.profile").is_file());
        assert_eq!(fs::read_to_string(&mods).unwrap(), "custom contents");
        for _ in 0..2 {
            run("reset");
            assert_eq!(fs::read(&mods).unwrap(), initial_mods);
            for path in [&prefs, &settings, &save] {
                assert_eq!(fs::read_to_string(path).unwrap(), "custom contents");
            }
        }
        assert!(!client.0.join("config").exists());
        assert!(!client.0.join("data").exists());
    }
}

#[test]
fn invalid_profile_commands_create_no_state() {
    let client = Client::new();
    for action in ["init", "reset"] {
        let profile = client.0.join("invalid=profile");
        let output = client
            .command(env!("CARGO_BIN_EXE_slopworld"))
            .args(["profile", action, "--profile"])
            .arg(&profile)
            .output()
            .unwrap();
        assert!(!output.status.success());
        assert!(String::from_utf8_lossy(&output.stderr).contains("contains '='"));
        assert!(!client.0.exists());
    }
}
