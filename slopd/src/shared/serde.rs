// Serialization helpers for the shared protocol. Generated code supplies tag mappings.

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
                Err(<D::Error as serde::de::Error>::unknown_variant(&value, &[$($value),+]))
            }
        }
    };
}
