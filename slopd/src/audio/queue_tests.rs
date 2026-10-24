use super::*;

#[test]
fn unpublished_blocks_are_empty_and_unconsumed_blocks_cannot_be_overwritten() {
    let (mut tx, mut rx) = channel(1);
    let mut samples = [0.0; CHUNK];
    // A descheduled producer may have copied samples without publishing the block.
    tx.shared.slots[0].samples[0].store(0.5f32.to_bits(), Ordering::Relaxed);
    assert_eq!(rx.try_recv(&mut samples), Err(TryRecvError::Empty));
    tx.try_send(&[0.25, -0.5]).unwrap();
    assert_eq!(tx.try_send(&[0.75]), Err(SendError::Full));
    assert_eq!(rx.try_recv(&mut samples), Ok(2));
    assert_eq!(&samples[..2], &[0.25, -0.5]);
    tx.try_send(&[0.75]).unwrap();
    drop(tx);
    assert_eq!(rx.try_recv(&mut samples), Ok(1));
    assert_eq!(samples[0], 0.75);
    assert_eq!(rx.try_recv(&mut samples), Err(TryRecvError::Disconnected));
    assert_eq!(rx.try_recv(&mut samples), Err(TryRecvError::Disconnected));
}

#[test]
fn closing_a_full_consumer_releases_the_producer() {
    let (mut tx, rx) = channel(1);
    tx.try_send(&[0.5]).unwrap();
    drop(rx);
    assert_eq!(tx.try_send(&[0.25]), Err(SendError::Disconnected));
}

#[test]
fn concurrent_wraparound_preserves_every_sample_and_the_final_partial_block() {
    let (mut tx, mut rx) = channel(2);
    let feeder = std::thread::spawn(move || {
        for value in 0..2000u32 {
            let samples = [Sample::from_bits(value); CHUNK];
            let len = if value == 1999 { 3 } else { CHUNK };
            loop {
                match tx.try_send(&samples[..len]) {
                    Ok(()) => break,
                    Err(SendError::Full) => std::thread::yield_now(),
                    Err(SendError::Disconnected | SendError::InvalidBlock) => {
                        panic!("consumer closed early")
                    }
                }
            }
        }
    });
    let mut samples = [0.0; CHUNK];
    let mut expected = 0u32;
    loop {
        match rx.try_recv(&mut samples) {
            Ok(len) => {
                assert_eq!(len, if expected == 1999 { 3 } else { CHUNK });
                assert!(samples[..len].iter().all(|s| s.to_bits() == expected));
                expected += 1;
            }
            Err(TryRecvError::Empty) => std::thread::yield_now(),
            Err(TryRecvError::Disconnected) => break,
        }
    }
    feeder.join().unwrap();
    assert_eq!(expected, 2000);
}

#[test]
fn invalid_blocks_do_not_publish_or_advance_the_producer() {
    let (mut tx, mut rx) = channel(1);
    assert_eq!(tx.try_send(&[]), Err(SendError::InvalidBlock));
    assert_eq!(tx.try_send(&[0.0; CHUNK + 1]), Err(SendError::InvalidBlock));
    tx.try_send(&[0.5]).unwrap();
    let mut samples = [0.0; CHUNK];
    assert_eq!(rx.try_recv(&mut samples), Ok(1));
    assert_eq!(samples[0], 0.5);
}
