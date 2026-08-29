# zsh terminal key bindings

## Key sequence mismatch

tmux sends the standard `\e[1~` (Home), `\e[4~` (End), `\e[3~` (Delete/DC) and
`\e[2~` (Insert/IC) sequences for these keys. The `tmux-256color` terminfo entries
`khome`, `kend`, `kdch1`, and `kich1` specify them.

`bash` readline reads terminfo and binds these correctly:
- `\e[1~` → `beginning-of-line`
- `\e[4~` → `end-of-line`
- `\e[3~` → `delete-char`

`zsh` ZLE does not. `bindkey -e` (the default emacs keymap) leaves `\e[1~`,
`\e[4~`, `\e[3~`, and `\e[2~` as `undefined-key`. When one of these keys is pressed,
zsh rings the bell and inserts `~` (the last character of the escape sequence) into the
command line.

| Key | tmux sends | bash | zsh |
|-----|-----------|------|-----|
| Home | `\e[1~` | beginning-of-line | undefined, bell, insert `~` |
| End | `\e[4~` | end-of-line | undefined, bell, insert `~` |
| Delete | `\e[3~` | delete-char | undefined, bell, insert `~` |
| Insert | `\e[2~` | overwrite-mode | undefined, bell, insert `~` |

Arrow keys (`\eOD`, `\eOB`, etc.) and the `^A`/`^E` emacs shortcuts work in
both shells. Only the `\e[1~`-family terminal codes are missing from zsh's
default keymap.

## Effect

The `~` insertion corrupts the command line. The typist hits Home expecting to
jump to the start of the line, and instead sees `~` appear and the cursor stay
put. Every subsequent keystroke builds on the corrupted line, which affects
keys, cursor position, and any text copied from the pane.

## Host and sandbox shells

The host terminal runs `$SHELL` = zsh. The sandboxed "shell" errand runs bash
(`[defaults] shell` = `bash.toml`). So the user's bash session works fine and
the zsh host session is the one with the unbound keys.

## Fix

Bind the missing keys. In the user's `~/.zshrc`:

```zsh
bindkey "\e[1~" beginning-of-line     # Home
bindkey "\e[4~" end-of-line           # End
bindkey "\e[3~" delete-char           # Delete
bindkey "\e[2~" overwrite-mode        # Insert
```

Or, for the sandboxed zsh preset, ensure these bindings are in place before
`zsh` starts — either by providing a `.zshrc` in the sandbox home, or running
`zsh -c "source ..."` with a setup file.

## zsh command-title escape

This `.zshrc` loads oh-my-zsh, whose `preexec` hook emits the tmux private
`ESC k <command> ESC \\` title sequence before command output. A VT parser that
does not recognize it displays the command name as screen text, so `echo one`
can look like `echoone`. `slopd` consumes this sequence in `emu.rs`, stores the
command as the pane title, and leaves only `one` in the screen output. No zshrc
workaround is needed.

## Verified

The `zsh -f` test in the sandbox and the host zsh both show the same behaviour:

```bash
# zsh does not bind \e[1~:
zsh -f -c 'bindkey "\e[1~"'
# → "undefined-key"

# bash does:
bash -c 'bind -p | grep "\\e[1~"'
# → "\\e[1~": beginning-of-line
```
