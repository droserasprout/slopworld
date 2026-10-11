use super::*;
use prost::Message;

#[tokio::test]
async fn root_listing_round_trips_without_a_parent() {
    let manager = crate::session::test_manager(crate::config::Config::default());
    let request = serde_json::from_value(serde_json::json!({"path": "/", "limit": 1})).unwrap();
    let Proto(reply) = browse(State(manager), Query(request)).await.unwrap();
    let decoded = wire::BrowseResult::decode(reply.encode_to_vec().as_slice()).unwrap();
    assert_eq!(decoded.path, "/");
    assert_eq!(decoded.parent, None);
}

#[tokio::test]
async fn listing_preserves_names_parent_and_truncation_on_the_wire() {
    let fixture = crate::api::handlers::tests::fixture("browse-wire");
    std::fs::create_dir(fixture.join("empty")).unwrap();
    std::fs::write(fixture.join("café.txt"), "text").unwrap();
    let manager = crate::session::test_manager(crate::config::Config::default());
    for limit in [1, 10] {
        let request = serde_json::from_value(serde_json::json!({
            "path": fixture.to_str().unwrap(), "files": "1", "limit": limit
        }))
        .unwrap();
        let Proto(reply) = browse(State(manager.clone()), Query(request))
            .await
            .unwrap();
        let reply = wire::BrowseResult::decode(reply.encode_to_vec().as_slice()).unwrap();
        assert_eq!(reply.parent.as_deref(), fixture.parent().unwrap().to_str());
        assert_eq!(reply.truncated, limit == 1);
        if limit == 10 {
            assert_eq!(reply.dirs, ["empty"]);
            assert_eq!(reply.empty_dirs, ["empty"]);
            assert_eq!(reply.files, ["café.txt"]);
        }
    }
}
