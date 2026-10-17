//! Values shared by library prompts and breadcrumbs.

use super::template;
use rand::seq::SliceRandom;
use std::{iter::Cycle, slice::Iter};

const MAGIC_8_BALL_ANSWERS: [&str; 20] = [
    "It is certain.",
    "It is decidedly so.",
    "Without a doubt.",
    "Yes definitely.",
    "You may rely on it.",
    "As I see it, yes.",
    "Most likely.",
    "Outlook good.",
    "Yes.",
    "Signs point to yes.",
    "Reply hazy, try again.",
    "Ask again later.",
    "Better not tell you now.",
    "Cannot predict now.",
    "Concentrate and ask again.",
    "Don't count on it.",
    "My reply is no.",
    "My sources say no.",
    "Outlook not so good.",
    "Very doubtful.",
];

pub(super) struct PromptVars<'a> {
    pub(super) agent: &'a str,
    pub(super) project: &'a str,
    pub(super) directory: &'a str,
    pub(super) command: &'a str,
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
            ("magic_8_ball", _) => MAGIC_8_BALL_ANSWERS
                .choose(&mut rand::thread_rng())
                .copied(),
            ("agent", Some(v)) => Some(v.agent),
            ("project", Some(v)) => Some(v.project),
            ("directory", Some(v)) => Some(v.directory),
            ("command", Some(v)) => Some(v.command),
            _ => None,
        }
    }
}

pub(super) fn render_prompt_with(
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

pub(super) fn render_prompt(text: &str, random_tips: &[String]) -> String {
    render_prompt_with(text, random_tips, None)
}

#[cfg(test)]
#[path = "prompt_tests.rs"]
mod tests;
