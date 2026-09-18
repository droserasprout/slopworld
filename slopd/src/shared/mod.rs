// Protocol bindings generated from shared/protocol.yaml, plus handwritten serialization helpers.
pub(crate) mod protocol;
mod serde;

#[allow(dead_code)]
pub(crate) mod wire {
    include!(concat!(env!("OUT_DIR"), "/slopworld.rs"));
}
pub(crate) mod http_wire;
