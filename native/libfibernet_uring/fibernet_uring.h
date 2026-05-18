/* fibernet_uring.h — Public C API for the FiberNet io_uring bridge.
 *
 * Build with CMake on Linux (liburing-dev required):
 *   cmake -B build -DCMAKE_BUILD_TYPE=Release && cmake --build build
 *
 * The resulting libfibernet_uring.so is loaded via P/Invoke from
 * FiberNet.Transport.Kestrel.IoUring when running on Linux kernel >= 5.1.
 */

#pragma once

#ifdef __cplusplus
extern "C" {
#endif

#include <stdint.h>
#include <stddef.h>

/* Opaque handle to an io_uring instance managed by this bridge. */
typedef struct fibernet_ring fibernet_ring_t;

/* CQE flags forwarded from the kernel (mirrors io_uring cqe->flags). */
#define FIBERNET_CQE_F_MORE   (1u << 1)   /* more completions follow (multishot) */
#define FIBERNET_CQE_F_NOTIF  (1u << 3)   /* SEND_ZC notification */

/* ── Ring lifecycle ────────────────────────────────────────────────────────── */

/**
 * Allocate and initialise a new io_uring with the given SQ depth.
 * Tries COOP_TASKRUN | SINGLE_ISSUER first; falls back to 0 on older kernels.
 * Returns NULL on failure (kernel < 5.1 or out-of-memory).
 */
fibernet_ring_t *fibernet_ring_create(unsigned queue_depth);

/** Release all kernel and heap resources owned by the ring. */
void fibernet_ring_destroy(fibernet_ring_t *ring);

/* ── SQE builders (do NOT call fibernet_ring_submit automatically) ─────────── */

/**
 * Post a single ACCEPT SQE.  The resulting CQE carries the accepted fd in res.
 * user_data is returned verbatim in the CQE so the caller can match it.
 * Returns 0 on success, -ENOBUFS if the SQ is full.
 */
int fibernet_ring_submit_accept(fibernet_ring_t *ring,
                                int              server_fd,
                                uint64_t         user_data);

/**
 * Post a RECV SQE.  buf must remain valid and pinned until the matching CQE.
 * Returns 0 on success, -ENOBUFS if SQ full.
 */
int fibernet_ring_submit_recv(fibernet_ring_t *ring,
                              int              fd,
                              void            *buf,
                              unsigned         len,
                              uint64_t         user_data);

/**
 * Post a SEND SQE.  buf must remain valid until the matching CQE.
 * Returns 0 on success, -ENOBUFS if SQ full.
 */
int fibernet_ring_submit_send(fibernet_ring_t *ring,
                              int              fd,
                              const void      *buf,
                              unsigned         len,
                              uint64_t         user_data);

/**
 * Post a SEND_ZC (zero-copy send) SQE (requires kernel >= 6.0).
 * Falls back to regular SEND on older kernels at compile time.
 * buf must remain valid until the FIBERNET_CQE_F_NOTIF completion.
 * Returns 0 on success, -ENOBUFS if SQ full.
 */
int fibernet_ring_submit_send_zc(fibernet_ring_t *ring,
                                 int              fd,
                                 const void      *buf,
                                 unsigned         len,
                                 uint64_t         user_data);

/**
 * Post a CLOSE SQE (closes fd asynchronously via the ring).
 * user_data = 0 is fine if the caller does not need to track the completion.
 */
int fibernet_ring_submit_close(fibernet_ring_t *ring,
                               int              fd,
                               uint64_t         user_data);

/**
 * Post a NOP SQE — used to unblock a blocked fibernet_ring_wait_cqe.
 * Passing user_data = UINT64_MAX is the shutdown sentinel convention.
 */
int fibernet_ring_submit_nop(fibernet_ring_t *ring, uint64_t user_data);

/* ── Flush & complete ─────────────────────────────────────────────────────── */

/**
 * Submit all pending SQEs to the kernel in a single syscall.
 * Returns the number of SQEs submitted (>= 0) or a negative errno on error.
 */
int fibernet_ring_submit(fibernet_ring_t *ring);

/**
 * Block until one CQE is available, consume it, and populate *out_*.
 * Returns 0 on success, -EINTR if interrupted (safe to retry), other negative
 * errno on fatal error.
 */
int fibernet_ring_wait_cqe(fibernet_ring_t *ring,
                           uint64_t        *out_user_data,
                           int32_t         *out_res,
                           uint32_t        *out_flags);

/**
 * Non-blocking CQE peek.  Returns 0 if a CQE was consumed, -EAGAIN if none.
 */
int fibernet_ring_peek_cqe(fibernet_ring_t *ring,
                           uint64_t        *out_user_data,
                           int32_t         *out_res,
                           uint32_t        *out_flags);

/* ── Utility ──────────────────────────────────────────────────────────────── */

/**
 * Create a non-blocking TCP server socket bound to 127.0.0.1:port,
 * with SO_REUSEADDR, SO_REUSEPORT and TCP_NODELAY set.
 * Returns the fd on success or a negative errno on failure.
 */
int fibernet_create_server_socket(int port, int backlog);

/** Close a file descriptor synchronously (not via the ring). */
int fibernet_close_fd(int fd);

/** Returns a static version string. */
const char *fibernet_uring_version(void);

#ifdef __cplusplus
}
#endif
