/* fibernet_uring.c — Implementation of the FiberNet io_uring bridge.
 *
 * Requires liburing (https://github.com/axboe/liburing).
 * Install on Debian/Ubuntu: sudo apt-get install liburing-dev
 */

#include "fibernet_uring.h"

#include <liburing.h>
#include <stdlib.h>
#include <string.h>
#include <errno.h>
#include <unistd.h>
#include <fcntl.h>

#include <netinet/in.h>
#include <netinet/tcp.h>
#include <arpa/inet.h>
#include <sys/socket.h>

/* ── Internal struct ────────────────────────────────────────────────────────── */

struct fibernet_ring {
    struct io_uring ring;
};

/* ── Ring lifecycle ─────────────────────────────────────────────────────────── */

fibernet_ring_t *fibernet_ring_create(unsigned queue_depth)
{
    fibernet_ring_t *r = (fibernet_ring_t *)calloc(1, sizeof(fibernet_ring_t));
    if (!r) { return NULL; }

    /* Prefer modern performance flags; fall back gracefully on older kernels. */
    unsigned flags = 0;
#ifdef IORING_SETUP_COOP_TASKRUN
    flags |= IORING_SETUP_COOP_TASKRUN;
#endif
#ifdef IORING_SETUP_SINGLE_ISSUER
    flags |= IORING_SETUP_SINGLE_ISSUER;
#endif

    if (flags != 0 && io_uring_queue_init(queue_depth, &r->ring, flags) < 0) {
        flags = 0; /* retry with no flags */
    }
    if (flags == 0 && io_uring_queue_init(queue_depth, &r->ring, 0) < 0) {
        free(r);
        return NULL;
    }
    return r;
}

void fibernet_ring_destroy(fibernet_ring_t *r)
{
    if (!r) { return; }
    io_uring_queue_exit(&r->ring);
    free(r);
}

/* ── SQE helper — flush and retry once if the SQ is full ────────────────────── */

static struct io_uring_sqe *get_sqe(fibernet_ring_t *r)
{
    struct io_uring_sqe *sqe = io_uring_get_sqe(&r->ring);
    if (!sqe) {
        io_uring_submit(&r->ring);
        sqe = io_uring_get_sqe(&r->ring);
    }
    return sqe;
}

/* ── SQE builders ───────────────────────────────────────────────────────────── */

int fibernet_ring_submit_accept(fibernet_ring_t *r, int server_fd, uint64_t user_data)
{
    struct io_uring_sqe *sqe = get_sqe(r);
    if (!sqe) { return -ENOBUFS; }
    io_uring_prep_accept(sqe, server_fd, NULL, NULL, SOCK_CLOEXEC);
    io_uring_sqe_set_data64(sqe, user_data);
    return 0;
}

int fibernet_ring_submit_recv(fibernet_ring_t *r,
                              int fd, void *buf, unsigned len, uint64_t user_data)
{
    struct io_uring_sqe *sqe = get_sqe(r);
    if (!sqe) { return -ENOBUFS; }
    io_uring_prep_recv(sqe, fd, buf, len, 0);
    io_uring_sqe_set_data64(sqe, user_data);
    return 0;
}

int fibernet_ring_submit_send(fibernet_ring_t *r,
                              int fd, const void *buf, unsigned len, uint64_t user_data)
{
    struct io_uring_sqe *sqe = get_sqe(r);
    if (!sqe) { return -ENOBUFS; }
    io_uring_prep_send(sqe, fd, buf, len, 0);
    io_uring_sqe_set_data64(sqe, user_data);
    return 0;
}

int fibernet_ring_submit_send_zc(fibernet_ring_t *r,
                                 int fd, const void *buf, unsigned len, uint64_t user_data)
{
    struct io_uring_sqe *sqe = get_sqe(r);
    if (!sqe) { return -ENOBUFS; }
#if defined(IORING_OP_SEND_ZC)
    /* SEND_ZC requires kernel >= 6.0 and liburing >= 2.3 */
    io_uring_prep_send_zc(sqe, fd, buf, len, 0, 0);
#else
    /* Fall back to regular send on older kernels */
    io_uring_prep_send(sqe, fd, buf, len, 0);
#endif
    io_uring_sqe_set_data64(sqe, user_data);
    return 0;
}

int fibernet_ring_submit_close(fibernet_ring_t *r, int fd, uint64_t user_data)
{
    struct io_uring_sqe *sqe = get_sqe(r);
    if (!sqe) { return -ENOBUFS; }
    io_uring_prep_close(sqe, fd);
    io_uring_sqe_set_data64(sqe, user_data);
    return 0;
}

int fibernet_ring_submit_nop(fibernet_ring_t *r, uint64_t user_data)
{
    struct io_uring_sqe *sqe = get_sqe(r);
    if (!sqe) { return -ENOBUFS; }
    io_uring_prep_nop(sqe);
    io_uring_sqe_set_data64(sqe, user_data);
    return 0;
}

/* ── Flush & complete ───────────────────────────────────────────────────────── */

int fibernet_ring_submit(fibernet_ring_t *r)
{
    return io_uring_submit(&r->ring);
}

int fibernet_ring_wait_cqe(fibernet_ring_t *r,
                           uint64_t *out_user_data,
                           int32_t  *out_res,
                           uint32_t *out_flags)
{
    struct io_uring_cqe *cqe = NULL;
    int ret = io_uring_wait_cqe(&r->ring, &cqe);
    if (ret < 0) { return ret; }

    *out_user_data = io_uring_cqe_get_data64(cqe);
    *out_res       = (int32_t)cqe->res;
    *out_flags     = cqe->flags;
    io_uring_cqe_seen(&r->ring, cqe);
    return 0;
}

int fibernet_ring_peek_cqe(fibernet_ring_t *r,
                           uint64_t *out_user_data,
                           int32_t  *out_res,
                           uint32_t *out_flags)
{
    struct io_uring_cqe *cqe = NULL;
    int ret = io_uring_peek_cqe(&r->ring, &cqe);
    if (ret < 0) { return ret; } /* -EAGAIN = no CQE available */

    *out_user_data = io_uring_cqe_get_data64(cqe);
    *out_res       = (int32_t)cqe->res;
    *out_flags     = cqe->flags;
    io_uring_cqe_seen(&r->ring, cqe);
    return 0;
}

/* ── Utility ────────────────────────────────────────────────────────────────── */

int fibernet_create_server_socket(int port, int backlog)
{
    int fd = socket(AF_INET, SOCK_STREAM | SOCK_NONBLOCK | SOCK_CLOEXEC, IPPROTO_TCP);
    if (fd < 0) { return -errno; }

    int yes = 1;
    setsockopt(fd, SOL_SOCKET,  SO_REUSEADDR, &yes, sizeof(yes));
    setsockopt(fd, SOL_SOCKET,  SO_REUSEPORT, &yes, sizeof(yes));
    setsockopt(fd, IPPROTO_TCP, TCP_NODELAY,  &yes, sizeof(yes));

    struct sockaddr_in addr;
    memset(&addr, 0, sizeof(addr));
    addr.sin_family      = AF_INET;
    addr.sin_port        = htons((uint16_t)port);
    addr.sin_addr.s_addr = htonl(INADDR_LOOPBACK);

    if (bind(fd, (const struct sockaddr *)&addr, sizeof(addr)) < 0) {
        int e = errno; close(fd); return -e;
    }
    if (listen(fd, backlog > 0 ? backlog : 512) < 0) {
        int e = errno; close(fd); return -e;
    }
    return fd;
}

int fibernet_close_fd(int fd)
{
    return (close(fd) == 0) ? 0 : -errno;
}

const char *fibernet_uring_version(void)
{
    return "fibernet-uring-1.0.0";
}
