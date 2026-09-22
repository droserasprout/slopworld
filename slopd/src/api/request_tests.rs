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

#[test]
fn worker_requests_default_durable_but_allow_explicit_one_shot() {
    let value: serde_json::Value = protobuf::domain(crate::shared::wire::SpawnWorkerReq {
        project: Some("repo".into()),
        template: Some("review".into()),
        body: Some("task".into()),
        ..Default::default()
    })
    .unwrap();
    let request: types::SpawnWorkerReq = serde_json::from_value(value.clone()).unwrap();
    assert!(request.durable);
    let mut one_shot = value;
    one_shot["durable"] = false.into();
    let request: types::SpawnWorkerReq = serde_json::from_value(one_shot).unwrap();
    assert!(!request.durable);
}
