use super::*;

#[test]
fn current_requests_reject_unknown_fields_without_compatibility_rewrites() {
    assert!(parse_session(json!({"name":"agent", "netwrok":"none"})).is_err());
    assert!(parse_project(json!({"name":"repo", "unexpected":true})).is_err());
    let project = parse_project(json!({"name":"repo", "dir":"/work/repo",
            "mounts":[{"from":"/work/shared", "to":"/mnt/shared", "mode":"ro"}]}))
    .unwrap();
    assert_eq!(project.mounts[0].from, "/work/shared");
    assert!(parse_template(json!({"name":"review", "defaults":{"unexpected":true}})).is_err());
}
