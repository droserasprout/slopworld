use super::*;

#[test]
fn subscribers_share_encoding_failures_without_retrying() {
    let message = EventMessage::new(Event::Usage {
        usage: Default::default(),
    });
    let subscriber = message.clone();
    assert_eq!(
        message.encode_with(|_| Err("invalid payload".into())),
        Err("invalid payload".into())
    );
    assert_eq!(
        subscriber.encode_with(|_| panic!("encoding failure must be cached")),
        Err("invalid payload".into())
    );
}
