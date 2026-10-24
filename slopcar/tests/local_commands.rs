//! Local queries must bypass container settings, Docker, and state creation.
#[test]
fn help_notices_and_paths_work_without_valid_container_settings() -> std::io::Result<()> {
    let root = tempfile::tempdir()?;
    let config = root.path().join("absent-config");
    for args in [
        vec!["--help"],
        vec!["completions", "bash"],
        vec!["completions", "zsh"],
        vec!["completions", "fish"],
        vec!["start", "--help"],
        vec!["help", "logs"],
        vec!["licenses"],
        vec!["paths", "config"],
    ] {
        let output = std::process::Command::new(env!("CARGO_BIN_EXE_slopcar"))
            .args(&args)
            .env_remove("HOME")
            .env("PATH", root.path())
            .env("SLOPCAR_PORT", "invalid")
            .env("SLOPD_CONFIG_ROOT", &config)
            .output()?;
        assert!(
            output.status.success(),
            "{args:?}: {}",
            String::from_utf8_lossy(&output.stderr)
        );
        assert!(!output.stdout.is_empty());
        assert!(!config.exists());
    }
    Ok(())
}

#[test]
fn completions_match_packaged_scripts() -> std::io::Result<()> {
    for (shell, relative) in [
        ("bash", "bash-completion/completions/slopcar"),
        ("zsh", "zsh/site-functions/_slopcar"),
        ("fish", "fish/vendor_completions.d/slopcar.fish"),
    ] {
        let output = std::process::Command::new(env!("CARGO_BIN_EXE_slopcar"))
            .args(["completions", shell])
            .output()?;
        assert!(output.status.success());
        let path = std::path::Path::new(env!("CARGO_MANIFEST_DIR"))
            .join("../packaging/completions")
            .join(relative);
        assert_eq!(output.stdout, std::fs::read(path)?);
    }
    Ok(())
}
