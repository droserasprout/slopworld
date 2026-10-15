use super::*;

#[tokio::test]
async fn checkout_identity_preserves_linked_paths_including_newlines() {
    for name in ["ordinary", "spaces ' quotes \" and 项目", "line\nbreak"] {
        let parent =
            std::env::temp_dir().join(format!("slop-identity-test-{}", uuid::Uuid::new_v4()));
        let root = parent.join(name);
        std::fs::create_dir_all(&root).unwrap();
        for args in [
            vec!["init", "-q"],
            vec![
                "-c",
                "user.name=Test",
                "-c",
                "user.email=test@example.invalid",
                "commit",
                "-q",
                "--allow-empty",
                "-m",
                "initial",
            ],
        ] {
            assert!(
                std::process::Command::new("git")
                    .arg("-C")
                    .arg(&root)
                    .args(args)
                    .status()
                    .unwrap()
                    .success()
            );
        }
        let checkout = root.join(if name.contains('\n') {
            "checkout\n"
        } else {
            "checkout"
        });
        std::fs::create_dir(&checkout).unwrap();
        crate::worktrees::allocate(&root, &checkout, "checkout", "HEAD")
            .await
            .unwrap();
        let identity = checkout_identity(&checkout).await.unwrap();
        assert_eq!(identity.root, checkout.canonicalize().unwrap());
        assert_eq!(
            identity.common_dir,
            root.join(".git").canonicalize().unwrap()
        );
        // A nested invocation still identifies the checkout root, not its cwd.
        let nested = checkout.join("nested");
        std::fs::create_dir(&nested).unwrap();
        let nested_identity = checkout_identity(&nested).await.unwrap();
        assert_eq!(nested_identity.root, identity.root);
        assert_eq!(nested_identity.common_dir, identity.common_dir);
        std::fs::remove_dir_all(parent).unwrap();
    }
}
