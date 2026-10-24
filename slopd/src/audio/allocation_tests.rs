//! Count heap operations only on the test thread while exercising steady-state sample handoff.

use std::alloc::{GlobalAlloc, Layout, System};
use std::cell::Cell;

thread_local! {
    static COUNTS: Cell<Option<(usize, usize)>> = const { Cell::new(None) };
}

struct TrackedAllocator;

fn record(allocation: bool) {
    COUNTS
        .try_with(|counts| {
            if let Some((allocations, frees)) = counts.get() {
                counts.set(Some((
                    allocations + usize::from(allocation),
                    frees + usize::from(!allocation),
                )));
            }
        })
        .unwrap_or(());
}

// SAFETY: Every allocation operation is forwarded unchanged to System. The thread-local
// counters contain only integers and cannot themselves allocate or inspect allocated memory.
unsafe impl GlobalAlloc for TrackedAllocator {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        record(true);
        // SAFETY: Forward the caller's valid layout to the underlying allocator.
        unsafe { System.alloc(layout) }
    }
    unsafe fn alloc_zeroed(&self, layout: Layout) -> *mut u8 {
        record(true);
        // SAFETY: Forward the caller's valid layout to the underlying allocator.
        unsafe { System.alloc_zeroed(layout) }
    }
    unsafe fn dealloc(&self, ptr: *mut u8, layout: Layout) {
        record(false);
        // SAFETY: This allocation came from System, with the caller-supplied original layout.
        unsafe { System.dealloc(ptr, layout) }
    }
    unsafe fn realloc(&self, ptr: *mut u8, layout: Layout, new_size: usize) -> *mut u8 {
        record(true);
        record(false);
        // SAFETY: Forward System's allocation and the caller's valid size/layout unchanged.
        unsafe { System.realloc(ptr, layout, new_size) }
    }
}

#[global_allocator]
static ALLOCATOR: TrackedAllocator = TrackedAllocator;

#[test]
fn chunk_handoff_wraparound_and_rebuffering_do_not_allocate_or_free() {
    use super::queue::{self, CHUNK};
    use super::ring::enqueue_chunk;
    use std::sync::atomic::Ordering;

    let (old_tx, mut ring) = super::test_support::ring();
    drop(old_tx);
    // Use two slots so every iteration exercises reuse, not just initially empty slots.
    let (mut tx, small_rx) = queue::channel(2);
    ring.rx = small_rx;
    let title = super::title::TitleSink::pending(None, 0);
    let samples = [0.25; CHUNK];
    COUNTS.with(|counts| counts.set(Some((0, 0))));
    for _ in 0..12 {
        assert!(enqueue_chunk(
            &mut tx,
            &samples,
            &ring.ready,
            &ring.queued,
            1,
            &title
        ));
        for _ in 0..CHUNK {
            assert_eq!(ring.next(), Some(0.25));
        }
        assert_eq!(ring.next(), Some(0.0));
    }
    let counts = COUNTS.with(|counts| counts.replace(None)).unwrap();
    assert_eq!(counts, (0, 0));
    assert_eq!(ring.underruns.load(Ordering::Relaxed), 12);
}

#[test]
fn mixer_conversion_and_volume_do_not_allocate_or_free_during_playback() {
    use super::output_source::OutputSource;
    use super::queue::CHUNK;
    use rodio::{ChannelCount, SampleRate};
    use std::sync::Arc;
    use std::sync::atomic::{AtomicU32, Ordering};

    for (channels, rate) in [(1, 22050), (2, 44100), (2, 48000)] {
        let (mut tx, mut ring) = super::test_support::ring();
        ring.channels = ChannelCount::new(channels).unwrap();
        ring.rate = SampleRate::new(rate).unwrap();
        for _ in 0..16 {
            tx.try_send(&[0.5; CHUNK]).unwrap();
        }
        let (mixer, mut output) = rodio::mixer::mixer(
            ChannelCount::new(2).unwrap(),
            SampleRate::new(44100).unwrap(),
        );
        let volume = Arc::new(AtomicU32::new(1.0f32.to_bits()));
        mixer.add(OutputSource::new(Box::new(ring), volume.clone()));
        // Attach the pending source and prime converters before measuring steady state.
        for _ in 0..100 {
            assert_eq!(output.next(), Some(0.5));
        }
        COUNTS.with(|counts| counts.set(Some((0, 0))));
        volume.store(0.5f32.to_bits(), Ordering::Relaxed);
        for _ in 0..40000 {
            assert!(output.next().is_some());
        }
        let counts = COUNTS.with(|counts| counts.replace(None)).unwrap();
        assert_eq!(counts, (0, 0), "{channels} channels at {rate} Hz");
        assert_eq!(output.next(), Some(0.25));
    }
}
