use super::*;

#[test]
fn established_ids_remain_valid_without_accepting_unsafe_components() {
    assert!(valid_persistent("0123456789abcdef"));
    assert!(valid_persistent("11111111-1111-4111-8111-111111111111"));
    assert!(valid_task("00192abcdef01-0001"));
    assert!(valid_task("0123456789abcdef"));
    for id in [
        "../escape",
        "00192abcdef01-0001/child",
        "00192abcdef01--0001",
        "00192abcdeF01-0001",
        "001-0001",
    ] {
        assert!(!valid_persistent(id));
        assert!(!valid_task(id));
    }
}

#[test]
fn generated_ids_have_exact_lowercase_hex_format() {
    for _ in 0..100 {
        assert!(valid(&generate().unwrap()));
    }
    for id in [
        "",
        "0123456789abcde",
        "0123456789abcdef0",
        "0123456789abcdeF",
        "../3456789abcdef",
        "0123456789abcdeg",
    ] {
        assert!(!valid(id), "{id}");
    }
}

#[test]
fn collisions_retry_but_namespace_errors_abort() {
    let mut ids = ["0000000000000000", "0000000000000001"].into_iter();
    let id = allocate_with(
        || Ok(ids.next().unwrap().into()),
        |id| Ok(id == "0000000000000000"),
    )
    .unwrap();
    assert_eq!(id, "0000000000000001");
    let error = allocate_with(
        || Ok("0000000000000000".into()),
        |_| anyhow::bail!("unreadable namespace"),
    )
    .unwrap_err();
    assert_eq!(error.to_string(), "unreadable namespace");
}
