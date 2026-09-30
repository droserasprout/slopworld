// Protocol bindings generated from shared/protocol.yaml, plus handwritten serialization helpers.
pub(crate) mod protocol;
mod serde;

#[expect(
    dead_code,
    reason = "the protobuf schema includes types used by other consumers"
)]
pub(crate) mod wire {
    include!(concat!(env!("OUT_DIR"), "/slopworld.rs"));
}
pub(crate) mod http_wire;
