// Serialization helpers for the shared protocol; tag mappings are generated.

#[macro_export]
macro_rules! wire_enum {
    ($ty:ty, { $($variant:path => $value:path),+ $(,)? }) => {
        impl serde::Serialize for $ty {
            fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
            where S: serde::Serializer {
                let value = match self {
                    $($variant => $value,)+
                };
                serializer.serialize_str(value)
            }
        }
        impl<'de> serde::Deserialize<'de> for $ty {
            fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
            where D: serde::Deserializer<'de> {
                let value = <String as serde::Deserialize>::deserialize(deserializer)?;
                $(if value == $value { return Ok($variant); })+
                Err(<D::Error as serde::de::Error>::unknown_variant(&value, &[]))
            }
        }
    };
}

#[macro_export]
macro_rules! wire_event_serialize {
    ($ty:ty, { $( $variant:ident { $( $field:ident ),+ $(,)? } ),+ $(,)? }) => {
        impl serde::Serialize for $ty {
            fn serialize<S>(&self, serializer: S) -> Result<S::Ok, S::Error>
            where S: serde::Serializer {
                use serde::ser::SerializeStruct;
                let mut out = serializer.serialize_struct(stringify!($ty), 2)?;
                match self {
                    $(Self::$variant { $( $field ),+ } => {
                        out.serialize_field("t", $crate::wire_event_tag!($variant))?;
                        $(out.serialize_field(stringify!($field), $field)?;)+
                    }),+
                }
                out.end()
            }
        }
    };
}

#[macro_export]
macro_rules! wire_client_msg_payload {
    ($value:ident, strip_tag) => {{
        let mut payload = $value;
        payload
            .as_object_mut()
            .expect("websocket message tag came from a JSON object")
            .remove("t");
        payload
    }};
    ($value:ident) => {
        $value
    };
}

#[macro_export]
macro_rules! wire_client_msg_construct {
    ($variant:ident, $req:ident, { $( $field:ident ),+ $(,)? }) => {
        Self::$variant { $( $field: $req.$field ),+ }
    };
    ($variant:ident, $req:ident) => { Self::$variant($req) };
}

#[macro_export]
macro_rules! wire_client_msg_deserialize {
    (
        $ty:ty, {
            $(
                $variant:ident
                $( { $( $field:ident ),+ $(,)? } )?
                => $payload:ty $( [ $mode:ident ] )?
            ),+ $(,)?
        }
    ) => {
        impl<'de> serde::Deserialize<'de> for $ty {
            fn deserialize<D>(deserializer: D) -> Result<Self, D::Error>
            where D: serde::Deserializer<'de> {
                use serde::de::Error;
                let value = serde_json::Value::deserialize(deserializer)?;
                let tag = value
                    .get("t")
                    .and_then(serde_json::Value::as_str)
                    .ok_or_else(|| D::Error::custom("websocket message is missing tag t"))?;
                match tag {
                    $( $crate::wire_client_msg_tag!($variant) => {
                        let req: $payload = serde_json::from_value(
                            $crate::wire_client_msg_payload!(value $(, $mode)?)
                        )
                        .map_err(D::Error::custom)?;
                        Ok($crate::wire_client_msg_construct!($variant, req $(, { $( $field ),+ })?))
                    }),+
                    other => Err(D::Error::unknown_variant(other, &[])),
                }
            }
        }
    };
}
