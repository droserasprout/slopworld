//! Library prompt lookup and contextual composition shared by manual and automated input.

use crate::config::Config;
use anyhow::{Result, anyhow};

mod template;
use rand::seq::IteratorRandom;
use std::{iter::Cycle, slice::Iter};

const MAGIC_8_BALL_ANSWERS: &str = include_str!("../../../shared/magic-8-ball.txt");

pub(crate) struct PromptVars<'a> {
    pub(crate) agent: &'a str,
    pub(crate) project: &'a str,
    pub(crate) directory: &'a str,
    pub(crate) command: &'a str,
}

/// Tips are selected by the client; each occurrence advances through that list.
struct PromptContext<'a> {
    vars: Option<&'a PromptVars<'a>>,
    tips: Cycle<Iter<'a, String>>,
}

impl<'a> PromptContext<'a> {
    fn resolve(&mut self, key: &str) -> Option<&'a str> {
        match (key, self.vars) {
            ("random_tip", _) => self.tips.next().map(String::as_str),
            ("magic_8_ball", _) => MAGIC_8_BALL_ANSWERS.lines().choose(&mut rand::thread_rng()),
            ("agent", Some(v)) => Some(v.agent),
            ("project", Some(v)) => Some(v.project),
            ("directory", Some(v)) => Some(v.directory),
            ("command", Some(v)) => Some(v.command),
            _ => None,
        }
    }
}

pub(crate) fn render_prompt_with(
    text: &str,
    random_tips: &[String],
    vars: Option<&PromptVars>,
) -> String {
    // Restart the sequence for each render and wrap when placeholders outnumber tips.
    let mut context = PromptContext {
        vars,
        tips: random_tips.iter().cycle(),
    };
    template::render(text, |key| context.resolve(key))
}

pub(crate) fn render_prompt(text: &str, random_tips: &[String]) -> String {
    render_prompt_with(text, random_tips, None)
}

/// Compose a selected prompt from the caller's resolved session/project snapshot.
pub(crate) fn prepare_prompt(
    cfg: &Config,
    s: &crate::config::SessionCfg,
    p: &crate::config::ProjectCfg,
    prompt: &str,
    random_tips: &[String],
) -> Result<String> {
    let b = cfg
        .library_item(prompt)
        .filter(|b| b.kind == crate::config::LibraryItemKind::Prompt)
        .ok_or_else(|| anyhow!("no such prompt: {prompt}"))?;
    let command = cfg.command_of(s);
    let directory = crate::config::expand(&p.dir);
    let vars = crate::prompt::PromptVars {
        agent: &s.name,
        project: &p.name,
        directory: &directory,
        command: &command,
    };
    Ok(crate::prompt::render_prompt_with(
        &b.text,
        random_tips,
        Some(&vars),
    ))
}

#[cfg(test)]
#[path = "tests.rs"]
mod tests;
