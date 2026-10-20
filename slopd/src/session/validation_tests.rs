use super::*;

#[test]
fn generated_names_preserve_unicode_and_literal_hyphens_with_caller_fallbacks() {
    for fallback in ["library", "shell"] {
        assert_eq!(name_slug(" \t.:/--\n", fallback), fallback);
        assert_eq!(name_slug(" /café.\t工具::run/ ", fallback), "café-工具-run");
        assert_eq!(name_slug("keep--dashes", fallback), "keep--dashes");
    }
}
