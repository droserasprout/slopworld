use super::*;

#[test]
fn delegation_requires_explicit_enablement_and_honors_template_selection() {
    let mut policy: WorkerPolicy = toml::from_str("").unwrap();
    assert!(!policy.allows("review"));
    policy.enabled = true;
    assert!(policy.allows("review"));
    policy.all_templates = false;
    assert!(!policy.allows("review"));
    policy.templates.insert("review".into());
    assert!(policy.allows("review"));
    assert!(!policy.allows("team-review"));
    policy.enabled = false;
    assert!(!policy.allows("review"));
}
