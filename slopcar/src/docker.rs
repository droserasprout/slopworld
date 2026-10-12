//! Docker CLI transport; lifecycle owns decisions and security flag assembly.
use anyhow::{Context, Result, bail};
use std::{
    ffi::{OsStr, OsString},
    process::{Command, Stdio},
};

pub(crate) struct Docker {
    executable: OsString,
    #[cfg(test)]
    script: Option<OsString>,
}
impl Docker {
    pub fn new() -> Self {
        Self {
            executable: "docker".into(),
            #[cfg(test)]
            script: None,
        }
    }

    #[cfg(test)]
    pub fn script(path: &std::path::Path) -> Self {
        Self {
            executable: "/bin/sh".into(),
            script: Some(path.as_os_str().into()),
        }
    }

    fn command(&self) -> Command {
        let command = Command::new(&self.executable);
        #[cfg(test)]
        if let Some(script) = &self.script {
            let mut command = command;
            command.arg(script);
            return command;
        }
        command
    }

    pub fn ready(&self) -> Result<()> {
        if !self.probe(&["info".into()])? {
            bail!("Docker is not running or is inaccessible");
        }
        Ok(())
    }

    pub fn probe(&self, arguments: &[OsString]) -> Result<bool> {
        Ok(self
            .command()
            .args(arguments)
            .stdout(Stdio::null())
            .stderr(Stdio::null())
            .status()
            .context("could not run Docker; install Docker and ensure it is on PATH")?
            .success())
    }

    pub fn run(&self, arguments: &[OsString], quiet: bool) -> Result<()> {
        let mut command = self.command();
        command.args(arguments);
        if quiet {
            command.stdout(Stdio::null());
        }
        let status = command.status().context("could not run Docker")?;
        if !status.success() {
            bail!(
                "Docker {} failed ({status})",
                arguments
                    .first()
                    .map(|s| s.to_string_lossy())
                    .unwrap_or_default()
            );
        }
        Ok(())
    }

    pub fn exists(&self, kind: &str, name: &OsStr) -> Result<bool> {
        self.probe(&[kind.into(), "inspect".into(), name.into()])
    }
}
