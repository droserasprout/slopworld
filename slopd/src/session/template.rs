//! Prompt and breadcrumb template rendering.

pub(super) struct TemplateVars<'a> {
    pub(super) agent: &'a str,
    pub(super) project: &'a str,
    pub(super) directory: &'a str,
    pub(super) command: &'a str,
}

pub(super) fn render_template_with(
    text: &str,
    random_tips: &[String],
    vars: Option<&TemplateVars>,
) -> String {
    let mut next = 0usize;
    let mut out = String::with_capacity(text.len());
    let mut rest = text;
    while let Some(start) = rest.find("{{") {
        let Some(end_rel) = rest[start + 2..].find("}}") else {
            break;
        };
        let end = start + 2 + end_rel;
        out.push_str(&rest[..start]);
        let key = rest[start + 2..end].trim();
        let value = match (key, vars) {
            ("random_tip", _) if !random_tips.is_empty() => {
                let value = &random_tips[next % random_tips.len()];
                next += 1;
                Some(value.as_str())
            }
            ("agent", Some(v)) => Some(v.agent),
            ("project", Some(v)) => Some(v.project),
            ("directory", Some(v)) => Some(v.directory),
            ("command", Some(v)) => Some(v.command),
            _ => None,
        };
        if let Some(value) = value {
            out.push_str(value);
        } else {
            out.push_str(&rest[start..end + 2]);
        }
        rest = &rest[end + 2..];
    }
    out.push_str(rest);
    out
}

pub(super) fn render_template(text: &str, random_tips: &[String]) -> String {
    render_template_with(text, random_tips, None)
}
