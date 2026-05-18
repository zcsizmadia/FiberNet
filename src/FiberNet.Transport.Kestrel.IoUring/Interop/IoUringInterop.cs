using System.Runtime.InteropServices;

// CA5392: DefaultDllImportSearchPaths required; CA5393: SafeDirectories is the approved value.
// The native libfibernet_uring.so is published alongside the managed assembly.
[assembly: DefaultDllImportSearchPaths(DllImportSearchPath.SafeDirectories)]

namespace FiberNet.Transport.Kestrel.IoUring.Interop;

/// <summary>
/// Source-generated P/Invoke declarations for the native libfibernet_uring bridge.
/// All methods are Linux-only; the caller must check
/// <see cref="RuntimeInformation.IsOSPlatform"/> before invoking.
/// </summary>
internal static partial class IoUringInterop
{
    private const string LibName = "fibernet_uring";

    // ── Ring lifecycle ────────────────────────────────────────────────────────

    [LibraryImport(LibName, EntryPoint = "fibernet_ring_create")]
    internal static partial nint RingCreate(uint queueDepth);

    [LibraryImport(LibName, EntryPoint = "fibernet_ring_destroy")]
    internal static partial void RingDestroy(nint ring);

    // ── SQE builders ─────────────────────────────────────────────────────────

    [LibraryImport(LibName, EntryPoint = "fibernet_ring_submit_accept")]
    internal static partial int RingSubmitAccept(nint ring, int serverFd, ulong userData);

    [LibraryImport(LibName, EntryPoint = "fibernet_ring_submit_recv")]
    internal static partial int RingSubmitRecv(nint ring, int fd, nint buf, uint len, ulong userData);

    [LibraryImport(LibName, EntryPoint = "fibernet_ring_submit_send")]
    internal static partial int RingSubmitSend(nint ring, int fd, nint buf, uint len, ulong userData);

    [LibraryImport(LibName, EntryPoint = "fibernet_ring_submit_send_zc")]
    internal static partial int RingSubmitSendZc(nint ring, int fd, nint buf, uint len, ulong userData);

    [LibraryImport(LibName, EntryPoint = "fibernet_ring_submit_close")]
    internal static partial int RingSubmitClose(nint ring, int fd, ulong userData);

    [LibraryImport(LibName, EntryPoint = "fibernet_ring_submit_nop")]
    internal static partial int RingSubmitNop(nint ring, ulong userData);

    // ── Flush & complete ──────────────────────────────────────────────────────

    [LibraryImport(LibName, EntryPoint = "fibernet_ring_submit")]
    internal static partial int RingSubmit(nint ring);

    [LibraryImport(LibName, EntryPoint = "fibernet_ring_wait_cqe")]
    internal static partial int RingWaitCqe(
        nint ring, out ulong userData, out int res, out uint flags);

    [LibraryImport(LibName, EntryPoint = "fibernet_ring_peek_cqe")]
    internal static partial int RingPeekCqe(
        nint ring, out ulong userData, out int res, out uint flags);

    // ── Utility ───────────────────────────────────────────────────────────────

    [LibraryImport(LibName, EntryPoint = "fibernet_create_server_socket")]
    internal static partial int CreateServerSocket(int port, int backlog);

    [LibraryImport(LibName, EntryPoint = "fibernet_close_fd")]
    internal static partial int CloseFd(int fd);
}
