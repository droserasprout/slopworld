//! Container lifecycle and security policy; persistence and mount checks stay with their owners.
use crate::{
    cli::{Command, HELP, Start},
    config,
    docker::Docker,
    mounts,
    settings::Settings,
};
use anyhow::{Result, bail};
use std::{ffi::OsString, io::Write};

fn args(values: &[&str]) -> Vec<OsString> {
    values.iter().map(OsString::from).collect()
}

fn security() -> Result<(tempfile::NamedTempFile, Vec<OsString>)> {
    // Docker consumes the profile synchronously during create/run. Keep it alive until
    // that command returns; no installed companion file or persistent host copy is needed.
    let mut profile = tempfile::NamedTempFile::new()?;
    profile.write_all(include_bytes!("../seccomp.json"))?;
    let mut flags = args(&["--cap-drop", "ALL", "--security-opt"]);
    let mut option = OsString::from("seccomp=");
    option.push(profile.path());
    flags.push(option);
    flags.extend(args(&[
        "--security-opt",
        "systempaths=unconfined",
        "--device",
        "/dev/net/tun",
        "--tmpfs",
        "/tmp:rw,nosuid,nodev,exec,size=1g,uid=1000,gid=1000,mode=1777",
        "--tmpfs",
        "/run:rw,nosuid,nodev,noexec,size=16m,uid=1000,gid=1000,mode=0755",
        "--tmpfs",
        "/home/slop/.cache:rw,nosuid,nodev,exec,size=1g,uid=1000,gid=1000,mode=0700",
    ]));
    Ok((profile, flags))
}

pub(crate) fn run(command: Command, settings: &Settings) -> Result<()> {
    run_with(command, settings, &Docker::new())
}

fn run_with(command: Command, settings: &Settings, docker: &Docker) -> Result<()> {
    if matches!(command, Command::Help) {
        print!("{HELP}");
        return Ok(());
    }
    docker.ready()?;
    match command {
        Command::Help => {}
        Command::Notices => print!("{}", crate::NOTICES),
        Command::Build(source) => {
            let source = source.canonicalize()?;
            let dockerfile = source.join("slopcar/Dockerfile");
            if !dockerfile.is_file() || !source.join("slopd/Cargo.toml").is_file() {
                bail!("--source must name a SlopWorld checkout");
            }
            let mut flags = args(&["build", "--tag", &settings.image, "--file"]);
            flags.push(dockerfile.into_os_string());
            flags.push(source.into_os_string());
            docker.run(&flags, false)?;
        }
        Command::Doctor => {
            require_image(docker, settings)?;
            let (_profile, security) = security()?;
            let mut flags = args(&["run", "--rm", "--user", "1000:1000", "--read-only"]);
            flags.extend(security);
            flags.extend(args(&[&settings.image, "slopcar-doctor"]));
            docker.run(&flags, false)?;
        }
        Command::Start(start) => start_container(start, settings, docker)?,
        command => {
            if !docker.exists("container", settings.container.as_ref())? {
                bail!("container {} does not exist", settings.container);
            }
            let shell = matches!(command, Command::Shell);
            let (mut flags, quiet) = match command {
                Command::Stop => (args(&["stop"]), true),
                Command::Restart => (args(&["restart"]), true),
                Command::Status => (
                    args(&[
                        "inspect",
                        "--format",
                        "{{.State.Status}}{{if .State.Health}} health={{.State.Health.Status}}{{end}}",
                    ]),
                    false,
                ),
                Command::Logs(options) => {
                    let mut flags = args(&["logs"]);
                    flags.extend(options);
                    (flags, false)
                }
                Command::Shell => (args(&["exec", "--interactive", "--tty"]), false),
                Command::Remove => (args(&["rm", "--force"]), true),
                _ => unreachable!(),
            };
            flags.push(settings.container.clone().into());
            if shell {
                flags.push("/bin/bash".into());
            }
            docker.run(&flags, quiet)?;
            if quiet {
                println!(
                    "slopcar: command completed for {}; persistent state remains",
                    settings.container
                );
            }
        }
    }
    Ok(())
}

fn require_image(docker: &Docker, settings: &Settings) -> Result<()> {
    if !docker.exists("image", settings.image.as_ref())? {
        bail!(
            "image {} is missing; run slopcar build --source CHECKOUT or docker pull {}",
            settings.image,
            settings.image
        );
    }
    Ok(())
}

fn start_container(start: Start, settings: &Settings, docker: &Docker) -> Result<()> {
    if docker.exists("container", settings.container.as_ref())? {
        if start.has_options() {
            bail!("the container already exists; remove it before changing mounts or budgets");
        }
        docker.run(&args(&["start", &settings.container]), true)?;
        println!("slopcar: started existing container {}", settings.container);
        return Ok(());
    }
    require_image(docker, settings)?;
    if start.workspaces.is_empty() {
        bail!("the first start needs at least one --workspace");
    }
    // Validate the complete plan before creating config or starting a container.
    let mut mount_flags = Vec::new();
    for (path, target) in [
        (&settings.config, "/home/slop/.config/slopworld"),
        (&settings.data, "/home/slop/.local/share/slopworld"),
    ] {
        mount_flags.extend(["--mount".into(), mounts::state(path, target)?.into()]);
    }
    for workspace in &start.workspaces {
        mount_flags.extend([
            "--mount".into(),
            mounts::workspace(workspace, settings)?.into(),
        ]);
    }
    for (readonly, credential) in &start.credentials {
        mount_flags.extend([
            "--mount".into(),
            mounts::credential(credential, *readonly, settings)?.into(),
        ]);
    }
    let port = start.port.unwrap_or(settings.port);
    let (_profile, security) = security()?;
    config::seed(&settings.config, &settings.data, port)?;
    let mut flags = args(&[
        "run",
        "--detach",
        "--name",
        &settings.container,
        "--hostname",
        "slopcar",
        "--user",
        "1000:1000",
        "--read-only",
        "--restart",
        "unless-stopped",
        "--stop-timeout",
        "15",
    ]);
    for (flag, value) in [
        (
            "--memory",
            start
                .memory
                .unwrap_or_else(|| settings.memory.clone().into()),
        ),
        (
            "--cpus",
            start.cpus.unwrap_or_else(|| settings.cpus.clone().into()),
        ),
        (
            "--pids-limit",
            start.pids.unwrap_or_else(|| settings.pids.clone().into()),
        ),
    ] {
        flags.extend([flag.into(), value]);
    }
    flags.extend(args(&[
        "--ulimit",
        "nofile=65536:65536",
        "--publish",
        &format!("127.0.0.1:{port}:{port}"),
        "--add-host",
        "host.docker.internal:host-gateway",
        "--env",
        "SLOPD_RUNTIME=slopcar",
        "--label",
        "io.slopworld.component=slopcar",
    ]));
    flags.extend(security);
    flags.extend(mount_flags);
    flags.push(settings.image.clone().into());
    docker.run(&flags, true)?;
    println!(
        "slopcar: started {} on http://127.0.0.1:{port}",
        settings.container
    );
    println!(
        "slopcar: endpoint and token: {}",
        settings.config.join("endpoint.toml").display()
    );
    Ok(())
}

#[cfg(test)]
#[path = "lifecycle_tests.rs"]
mod tests;
