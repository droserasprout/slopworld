//! Board selection follows project identity and is bounded by the spawning caller.
use super::*;

#[test]
fn omitted_settings_default_to_write_on_the_current_project() {
    let permissions: AgentPermissions = serde_json::from_str("{}").unwrap();
    assert_eq!(permissions.board_access, BoardAccess::Write);
    assert!(permissions.sees_board("one", Some("one")));
    assert!(!permissions.sees_board("one", Some("two")));
    assert!(!permissions.sees_board("root", None));
}

#[test]
fn worker_visibility_intersects_explicit_and_dynamic_project_selections() {
    let parent = AgentPermissions {
        board_access: BoardAccess::Read,
        visible_boards: ["root".into(), "shared".into()].into(),
        ..Default::default()
    };
    let template = AgentPermissions {
        board_access: BoardAccess::Admin,
        own_project_board: false,
        visible_boards: ["project".into(), "shared".into(), "outside".into()].into(),
        ..Default::default()
    };
    for (child, ceiling) in [(template.clone(), &parent), (parent.clone(), &template)] {
        let inherited = child.intersect(ceiling, "project");
        assert_eq!(inherited.board_access, BoardAccess::Read);
        assert!(!inherited.own_project_board);
        assert_eq!(
            inherited.visible_boards,
            ["project".into(), "shared".into()].into()
        );
    }
}
