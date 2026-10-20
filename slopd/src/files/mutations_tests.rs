use super::*;
use std::path::Path;
#[test]
fn file_names_are_one_component() {
    assert_eq!(entry_name("note.md").unwrap(), "note.md");
    entry_name("").unwrap_err();
    entry_name(".").unwrap_err();
    entry_name("src/note.md").unwrap_err();
    entry_name("src\\note.md").unwrap_err();
}

#[test]
fn file_paths_keep_whitespace_in_a_real_filename() {
    assert_eq!(
        file_path("/tmp/ notes.txt ").unwrap(),
        Path::new("/tmp/ notes.txt ")
    );
}
