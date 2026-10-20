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

#[test]
fn cloned_publishers_share_messages_and_recover_after_all_receivers_leave() {
    let publisher = EventPublisher::new(4);
    let runtime = publisher.clone();
    runtime.emit(Event::Usage {
        usage: Default::default(),
    });
    let mut first = publisher.subscribe();
    let mut second = publisher.subscribe();
    assert!(matches!(
        first.try_recv(),
        Err(tokio::sync::broadcast::error::TryRecvError::Empty)
    ));
    runtime.emit(Event::Usage {
        usage: Default::default(),
    });
    let a = first.try_recv().unwrap();
    let b = second.try_recv().unwrap();
    assert!(Arc::ptr_eq(&a, &b));
    assert!(matches!(a.event(), Event::Usage { .. }));
    drop(first);
    drop(second);
    runtime.emit(Event::Usage {
        usage: Default::default(),
    });
    let mut next = publisher.subscribe();
    publisher.emit(Event::Usage {
        usage: Default::default(),
    });
    assert!(matches!(
        next.try_recv().unwrap().event(),
        Event::Usage { .. }
    ));
}
