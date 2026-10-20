use super::*;

#[test]
fn file_action_paths_are_normalized_before_checking_project_containment() {
    let project = ProjectCfg {
        name: "repo".into(),
        dir: "/tmp/work/../repo".into(),
        ..Default::default()
    };
    assert_eq!(
        project_action_path(&project, "/tmp/repo/src/../file.rs").unwrap(),
        PathBuf::from("/tmp/repo/file.rs")
    );
    assert_eq!(
        project_action_path(&project, "/tmp/repo/../outside/file.rs")
            .unwrap_err()
            .to_string(),
        "File action path must be inside project \"repo\"."
    );
    assert_eq!(
        project_action_path(&project, " \t")
            .unwrap_err()
            .to_string(),
        "Enter a path for the file action."
    );
}

#[test]
fn file_action_paths_normalize_the_absolute_placeholder() {
    let path = normalize_path(Path::new("/tmp/slop/../repo/file name"));
    assert_eq!(path, PathBuf::from("/tmp/repo/file name"));
    assert_eq!(
        normalize_action_command(&path, "du -sh {{ absolute_path }}"),
        "du -sh '/tmp/repo/file name'"
    );
    assert_eq!(
        crate::sandbox::shell_split(&hold_action_command("du -sh '/tmp/repo/file name'")).unwrap(),
        vec![
            "bash",
            "-lc",
            "du -sh '/tmp/repo/file name'; exec \"${SHELL:-bash}\""
        ]
    );
}

#[test]
fn file_actions_stay_inside_the_project_root() {
    let project = ProjectCfg {
        name: "repo".into(),
        dir: "/tmp/slopworld-project".into(),
        ..Default::default()
    };
    assert_eq!(
        project_action_path(&project, "/tmp/slopworld-project/src/main.rs").unwrap(),
        PathBuf::from("/tmp/slopworld-project/src/main.rs")
    );
    let error = project_action_path(&project, "/tmp/slopworld-project-other/file")
        .unwrap_err()
        .to_string();
    assert!(error.contains("inside project"), "{error}");
}
