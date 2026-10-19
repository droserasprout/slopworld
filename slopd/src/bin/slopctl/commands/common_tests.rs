use super::*;

#[test]
fn components_preserve_unreserved_characters_and_escape_separators() {
    assert_eq!(encode_component("AZaz09-_.~"), "AZaz09-_.~");
    assert_eq!(
        encode_component("a/b?c=d&e+f #100%"),
        "a%2Fb%3Fc%3Dd%26e%2Bf%20%23100%25"
    );
    assert_eq!(encode_component("café\n"), "caf%C3%A9%0A");
    assert_eq!(encode_component(""), "");
}
