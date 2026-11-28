use super::*;
use std::sync::atomic::AtomicUsize;
use ureq::unversioned::transport::{Buffers, LazyBuffers, NextTimeout, Transport};

#[derive(Debug)]
struct InterruptedRead {
    buffers: LazyBuffers,
    calls: Arc<AtomicUsize>,
    cancelled: Arc<AtomicBool>,
    cancel_on_interrupt: bool,
}
impl Transport for InterruptedRead {
    fn buffers(&mut self) -> &mut dyn Buffers {
        &mut self.buffers
    }
    fn transmit_output(&mut self, _: usize, _: NextTimeout) -> Result<(), ureq::Error> {
        Err(io::Error::other("read fixture must not transmit").into())
    }
    fn await_input(&mut self, _: NextTimeout) -> Result<bool, ureq::Error> {
        if self.calls.fetch_add(1, Ordering::SeqCst) == 0 {
            if self.cancel_on_interrupt {
                self.cancelled.store(true, Ordering::Release);
            }
            return Err(ureq::Error::Io(io::Error::from(io::ErrorKind::Interrupted)));
        }
        Ok(true)
    }
    fn is_open(&mut self) -> bool {
        true
    }
}

#[test]
fn interrupted_reads_retry_but_recheck_cancellation_before_retrying() {
    for cancel_on_interrupt in [false, true] {
        let calls = Arc::new(AtomicUsize::new(0));
        let cancelled = Arc::new(AtomicBool::new(false));
        let mut transport = CancelTransport {
            inner: Box::new(InterruptedRead {
                buffers: LazyBuffers::new(128, 128),
                calls: calls.clone(),
                cancelled: cancelled.clone(),
                cancel_on_interrupt,
            }),
            cancelled,
            opening_deadline: Arc::new(AtomicU64::new(0)),
            idle_timeout: std::time::Duration::from_secs(1),
        };
        let result = transport.await_input(NextTimeout {
            after: std::time::Duration::from_secs(1).into(),
            reason: ureq::Timeout::RecvBody,
            per_read: None,
            per_write: None,
        });
        if cancel_on_interrupt {
            assert!(
                matches!(result, Err(ureq::Error::Io(error)) if error.kind() == io::ErrorKind::ConnectionAborted)
            );
            assert_eq!(calls.load(Ordering::SeqCst), 1);
        } else {
            assert!(result.unwrap());
            assert_eq!(calls.load(Ordering::SeqCst), 2);
        }
    }
}
