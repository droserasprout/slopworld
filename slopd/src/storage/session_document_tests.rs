use super::*;
use serde::de::{DeserializeOwned, Error, Visitor, value::Error as DecodeError};
use std::collections::BTreeSet;

// Ask Serde for every declared input field, including fields omitted by default
// serialization. This catches new optional fields without a hand-populated fixture.
struct FieldNames<'a>(&'a mut Vec<&'static str>);
impl<'de> serde::Deserializer<'de> for FieldNames<'_> {
    type Error = DecodeError;
    fn deserialize_any<V: Visitor<'de>>(self, _: V) -> std::result::Result<V::Value, Self::Error> {
        Err(DecodeError::custom("expected struct"))
    }
    fn deserialize_struct<V: Visitor<'de>>(
        self,
        _: &'static str,
        fields: &'static [&'static str],
        _: V,
    ) -> std::result::Result<V::Value, Self::Error> {
        self.0.extend_from_slice(fields);
        Err(DecodeError::custom("captured fields"))
    }
    serde::forward_to_deserialize_any! {
        bool i8 i16 i32 i64 u8 u16 u32 u64 f32 f64 char str string bytes byte_buf
        option unit unit_struct newtype_struct seq tuple tuple_struct map enum identifier ignored_any
    }
}
fn check<T: DeserializeOwned>(known: &[&str], storage_only: &[&str]) {
    let mut fields = Vec::new();
    assert!(T::deserialize(FieldNames(&mut fields)).is_err());
    assert!(!fields.is_empty());
    let model: BTreeSet<_> = fields
        .into_iter()
        .chain(storage_only.iter().copied())
        .collect();
    let schema: BTreeSet<_> = known.iter().copied().collect();
    assert_eq!(schema.len(), known.len(), "duplicate schema fields");
    assert_eq!(schema, model);
}

#[test]
fn document_field_metadata_covers_complete_serde_models() {
    check::<SessionCfg>(
        AGENT,
        &[
            "storage_order",
            "intent",
            "reader_label",
            "reader_path",
            "reader_key",
            "reader_scope",
            "reader_pinned",
            "reader_line",
            "worker_token",
            "daemon_token",
        ],
    );
    check::<crate::config::HostTerminalCfg>(HOST, &["storage_order"]);
    check::<crate::config::Limits>(LIMITS, &[]);
    check::<crate::config::WorkerPolicy>(WORKERS, &[]);
    check::<crate::config::DnsConfig>(DNS, &[]);
    check::<crate::presets::CommandPreset>(COMMAND, &[]);
    check::<crate::presets::SandboxPreset>(SANDBOX, &[]);
}
