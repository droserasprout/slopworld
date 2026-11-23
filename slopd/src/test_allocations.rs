//! Thread-local allocation measurements for synchronous and current-thread async tests.

use std::alloc::{GlobalAlloc, Layout, System};
use std::cell::Cell;

thread_local! {
    static COUNTS: Cell<Option<(usize, usize, usize)>> = const { Cell::new(None) };
}

struct TrackedAllocator;

fn record(allocation: bool, bytes: usize) {
    COUNTS
        .try_with(|counts| {
            if let Some((allocations, frees, total)) = counts.get() {
                counts.set(Some((
                    allocations + usize::from(allocation),
                    frees + usize::from(!allocation),
                    total + bytes,
                )));
            }
        })
        .unwrap_or(());
}

// SAFETY: Every allocation operation is forwarded unchanged to System. The thread-local
// counters contain only integers and cannot themselves allocate or inspect allocated memory.
unsafe impl GlobalAlloc for TrackedAllocator {
    unsafe fn alloc(&self, layout: Layout) -> *mut u8 {
        record(true, layout.size());
        // SAFETY: Forward the caller's valid layout to the underlying allocator.
        unsafe { System.alloc(layout) }
    }
    unsafe fn alloc_zeroed(&self, layout: Layout) -> *mut u8 {
        record(true, layout.size());
        // SAFETY: Forward the caller's valid layout to the underlying allocator.
        unsafe { System.alloc_zeroed(layout) }
    }
    unsafe fn dealloc(&self, ptr: *mut u8, layout: Layout) {
        record(false, 0);
        // SAFETY: This allocation came from System, with the caller-supplied original layout.
        unsafe { System.dealloc(ptr, layout) }
    }
    unsafe fn realloc(&self, ptr: *mut u8, layout: Layout, new_size: usize) -> *mut u8 {
        record(true, new_size);
        record(false, 0);
        // SAFETY: Forward System's allocation and the caller's valid size/layout unchanged.
        unsafe { System.realloc(ptr, layout, new_size) }
    }
}

#[global_allocator]
static ALLOCATOR: TrackedAllocator = TrackedAllocator;

pub(crate) fn start() {
    COUNTS.with(|counts| counts.set(Some((0, 0, 0))));
}
pub(crate) fn finish() -> (usize, usize, usize) {
    COUNTS.with(|counts| counts.replace(None)).unwrap()
}
