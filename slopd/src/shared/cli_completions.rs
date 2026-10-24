//! Render shell completions from the owning CLI tree without loading runtime state.

pub(crate) fn render(
    mut command: clap::Command,
    shell: clap_complete::Shell,
) -> Result<String, String> {
    let name = command.get_name().to_owned();
    let mut output = Vec::new();
    clap_complete::generate(shell, &mut command, name, &mut output);
    String::from_utf8(output).map_err(|error| error.to_string())
}
