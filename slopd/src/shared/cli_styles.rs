//! Shared help palette; each CLI's output writer owns terminal and NO_COLOR detection.

use clap::builder::Styles;
use clap::builder::styling::AnsiColor;

pub(crate) fn styles() -> Styles {
    Styles::styled()
        .header(AnsiColor::Green.on_default().bold())
        .usage(AnsiColor::Green.on_default().bold())
        .literal(AnsiColor::Cyan.on_default().bold())
        .placeholder(AnsiColor::Cyan.on_default())
}
