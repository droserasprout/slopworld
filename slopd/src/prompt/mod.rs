//! Values shared by library prompts and breadcrumbs.

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

#[cfg(test)]
#[path = "tests.rs"]
mod tests;
