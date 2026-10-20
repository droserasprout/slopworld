use super::*;

#[tokio::test]
async fn preview_handlers_preserve_literal_paths_and_scope() {
    let Some(root) = crate::test_support::isolated_with_env(|command, _| {
        command.env("SLOPD_PREVIEW_TEST_NAME", "other");
    }) else {
        return;
    };
    let manager = crate::application::test_application_with_socket(
        crate::config::Config::default(),
        format!("preview-{}", uuid::Uuid::new_v4()),
    );
    let scope = root.join("$SLOPD_PREVIEW_TEST_NAME");
    std::fs::create_dir(&scope).unwrap();
    for name in ["file ", "$SLOPD_PREVIEW_TEST_NAME"] {
        let path = scope.join(name);
        std::fs::write(&path, "literal contents").unwrap();
        let other = scope.join(if name == "file " { "file" } else { "other" });
        std::fs::write(&other, "wrong contents").unwrap();
        for scoped in [false, true] {
            let request = || {
                Query(ReadReq {
                    path: path.to_str().unwrap().to_owned(),
                    root: scoped.then(|| scope.to_str().unwrap().to_owned()),
                })
            };
            let text = read_file(State(manager.clone()), request())
                .await
                .unwrap()
                .0;
            assert_eq!(text.path, path.to_str().unwrap());
            assert_eq!(text.text, "literal contents");
            let image = read_image(State(manager.clone()), request())
                .await
                .unwrap()
                .0;
            assert_eq!(image.path, path.to_str().unwrap());
            assert_eq!(image.data, b"literal contents");
            let stat = file_stat(State(manager.clone()), request())
                .await
                .unwrap()
                .0;
            assert_eq!(stat.stamp, reader_stat(&path).await.unwrap().stamp);
        }
    }
}
