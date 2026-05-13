/*
 * soem_bridge.c
 *
 * Implements the context-handle API expected by SoemNative.cs.
 * Default build loads soem_wrapper.dll at runtime (ecx_* context API).
 * Define SOEM_BRIDGE_STATIC to link SOEM directly through soem.lib.
 *
 * Context handle design:
 *   CreateContext() allocates a SoemWrapper (ecx_contextt ctx as first member,
 *   plus an init_ok flag).  Because ctx is at offset 0, the returned
 *   (ecx_contextt *) is pointer-compatible with soem_wrapper.dll's ecx_SDOwrite.
 *   FreeContext() uses init_ok to guard against calling ecx_close on a context
 *   where ecx_init never succeeded (avoids DeleteCriticalSection on uninitialised
 *   port mutexes, which would crash).
 */

#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <strsafe.h>
#include <stdint.h>
#include <string.h>
#include <stdlib.h>
#include <wchar.h>

#include "soem/soem.h"

#ifndef ARRAYSIZE
#define ARRAYSIZE(a) (sizeof(a) / sizeof((a)[0]))
#endif

/* ── Context wrapper ──────────────────────────────────────────────────────── */
/*
 * ctx MUST be the first member so that (SoemWrapper *) == (ecx_contextt *)
 * and the handle returned by CreateContext() can be passed directly to
 * soem_wrapper.dll's ecx_SDOwrite without casting.
 */
typedef struct
{
    ecx_contextt ctx;
    int          init_ok; /* 1 after ecx_init succeeds; guards ecx_close in FreeContext */
    int          last_wkc;
    int          expected_wkc;
} SoemWrapper;

/* ── DLL handles ──────────────────────────────────────────────────────────── */
static HMODULE g_self   = NULL;
static int     g_loaded = 0;

#ifndef SOEM_BRIDGE_STATIC
static HMODULE g_wrapper = NULL;
#endif

/* ── Function pointer types ───────────────────────────────────────────────── */
typedef int     (__cdecl *PFN_ecx_init)(ecx_contextt *, const char *);
typedef int     (__cdecl *PFN_ecx_config_init)(ecx_contextt *);
typedef int     (__cdecl *PFN_ecx_config_map_group)(ecx_contextt *, void *, uint8);
typedef boolean (__cdecl *PFN_ecx_configdc)(ecx_contextt *);
typedef void    (__cdecl *PFN_ecx_dcsync0)(ecx_contextt *, uint16, boolean, uint32, int32);
typedef void    (__cdecl *PFN_ecx_close)(ecx_contextt *);
typedef int     (__cdecl *PFN_ecx_readstate)(ecx_contextt *);
typedef int     (__cdecl *PFN_ecx_writestate)(ecx_contextt *, uint16);
typedef uint16  (__cdecl *PFN_ecx_statecheck)(ecx_contextt *, uint16, uint16, int);
typedef int     (__cdecl *PFN_ecx_send)(ecx_contextt *);
typedef int     (__cdecl *PFN_ecx_receive)(ecx_contextt *, int);
typedef int     (__cdecl *PFN_ecx_SDOread)(ecx_contextt *, uint16, uint16, uint8,
                                            boolean, int *, void *, int);
typedef int     (__cdecl *PFN_ecx_SDOwrite)(ecx_contextt *, uint16, uint16, uint8,
                                             boolean, int, const void *, int);

#ifdef SOEM_BRIDGE_STATIC
static PFN_ecx_init             p_init        = ecx_init;
static PFN_ecx_config_init      p_config_init = ecx_config_init;
static PFN_ecx_config_map_group p_config_map  = ecx_config_map_group;
static PFN_ecx_configdc         p_configdc    = ecx_configdc;
static PFN_ecx_dcsync0          p_dcsync0     = ecx_dcsync0;
static PFN_ecx_close            p_close       = ecx_close;
static PFN_ecx_readstate        p_readstate   = ecx_readstate;
static PFN_ecx_writestate       p_writestate  = ecx_writestate;
static PFN_ecx_statecheck       p_statecheck  = ecx_statecheck;
static PFN_ecx_send             p_send        = ecx_send_processdata;
static PFN_ecx_receive          p_receive     = ecx_receive_processdata;
static PFN_ecx_SDOread          p_sdoread     = ecx_SDOread;
static PFN_ecx_SDOwrite         p_sdowrite    = ecx_SDOwrite;
#else
static PFN_ecx_init             p_init        = NULL;
static PFN_ecx_config_init      p_config_init = NULL;
static PFN_ecx_config_map_group p_config_map  = NULL;
static PFN_ecx_configdc         p_configdc    = NULL;
static PFN_ecx_dcsync0          p_dcsync0     = NULL;
static PFN_ecx_close            p_close       = NULL;
static PFN_ecx_readstate        p_readstate   = NULL;
static PFN_ecx_writestate       p_writestate  = NULL;
static PFN_ecx_statecheck       p_statecheck  = NULL;
static PFN_ecx_send             p_send        = NULL;
static PFN_ecx_receive          p_receive     = NULL;
static PFN_ecx_SDOread          p_sdoread     = NULL;
static PFN_ecx_SDOwrite         p_sdowrite    = NULL;
#endif

/* ── Loader ───────────────────────────────────────────────────────────────── */

#ifdef SOEM_BRIDGE_STATIC
static int load_wrapper(void)
{
    g_loaded = 1;
    return 1;
}
#else
static FARPROC get_proc(const char *name)
{
    return (g_wrapper != NULL) ? GetProcAddress(g_wrapper, name) : NULL;
}

static int load_wrapper(void)
{
    wchar_t selfPath[MAX_PATH];
    wchar_t wrapperPath[MAX_PATH];

    if (g_wrapper != NULL)
        return 1;

    if (g_self != NULL &&
        GetModuleFileNameW(g_self, selfPath, ARRAYSIZE(selfPath)) > 0)
    {
        wchar_t *slash = wcsrchr(selfPath, L'\\');
        if (slash != NULL)
        {
            *(slash + 1) = L'\0';
            if (SUCCEEDED(StringCchCopyW(wrapperPath, ARRAYSIZE(wrapperPath), selfPath)) &&
                SUCCEEDED(StringCchCatW(wrapperPath, ARRAYSIZE(wrapperPath), L"soem_wrapper.dll")))
            {
                g_wrapper = LoadLibraryW(wrapperPath);
            }
        }
    }
    if (g_wrapper == NULL)
        g_wrapper = LoadLibraryW(L"soem_wrapper.dll");
    if (g_wrapper == NULL)
        return 0;

    p_init        = (PFN_ecx_init)            get_proc("ecx_init");
    p_config_init = (PFN_ecx_config_init)     get_proc("ecx_config_init");
    p_config_map  = (PFN_ecx_config_map_group)get_proc("ecx_config_map_group");
    p_configdc    = (PFN_ecx_configdc)         get_proc("ecx_configdc");
    p_dcsync0     = (PFN_ecx_dcsync0)          get_proc("ecx_dcsync0");
    p_close       = (PFN_ecx_close)            get_proc("ecx_close");
    p_readstate   = (PFN_ecx_readstate)        get_proc("ecx_readstate");
    p_writestate  = (PFN_ecx_writestate)       get_proc("ecx_writestate");
    p_statecheck  = (PFN_ecx_statecheck)       get_proc("ecx_statecheck");
    p_send        = (PFN_ecx_send)             get_proc("ecx_send_processdata");
    p_receive     = (PFN_ecx_receive)          get_proc("ecx_receive_processdata");
    p_sdoread     = (PFN_ecx_SDOread)          get_proc("ecx_SDOread");
    p_sdowrite    = (PFN_ecx_SDOwrite)         get_proc("ecx_SDOwrite");

    if (!p_init || !p_config_init || !p_config_map || !p_close ||
        !p_readstate || !p_writestate || !p_statecheck ||
        !p_send || !p_receive || !p_sdoread || !p_sdowrite)
    {
        FreeLibrary(g_wrapper);
        g_wrapper = NULL;
        return 0;
    }

    g_loaded = 1;
    return 1;
}
#endif

/* ── DllMain ──────────────────────────────────────────────────────────────── */

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)reserved;
    if (reason == DLL_PROCESS_ATTACH)
    {
        g_self = instance;
        DisableThreadLibraryCalls(instance);
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
#ifndef SOEM_BRIDGE_STATIC
        if (g_wrapper != NULL)
        {
            FreeLibrary(g_wrapper);
            g_wrapper = NULL;
        }
#endif
    }
    return TRUE;
}

/* ── Public API ───────────────────────────────────────────────────────────── */

/*
 * CreateContext
 * Allocates and zero-initialises a SoemWrapper.
 * Returns &wrapper->ctx cast to ecx_contextt * (same address, compatible pointer).
 */
static int expected_wkc(ecx_contextt *context)
{
    if (context == NULL)
        return 0;

    return (context->grouplist[0].outputsWKC * 2) + context->grouplist[0].inputsWKC;
}

static void update_wkc(ecx_contextt *context, int wkc)
{
    if (context == NULL)
        return;

    SoemWrapper *w = (SoemWrapper *)context; /* ctx is at offset 0 */
    w->last_wkc = wkc;
    w->expected_wkc = expected_wkc(context);
}

ecx_contextt * CreateContext(void)
{
    if (!load_wrapper())
        return NULL;

    SoemWrapper *w = (SoemWrapper *)calloc(1, sizeof(SoemWrapper));
    return (w != NULL) ? &w->ctx : NULL;
}

/*
 * FreeContext
 * Calls ecx_close only if ecx_init previously succeeded (init_ok == 1),
 * then frees the SoemWrapper allocation.
 *
 * Calling ecx_close on a context where ecx_init never ran would call
 * DeleteCriticalSection on zero-initialised (calloc) CRITICAL_SECTIONs,
 * which is undefined behaviour and causes an access violation.
 */
void FreeContext(ecx_contextt *context)
{
    if (context == NULL)
        return;

    SoemWrapper *w = (SoemWrapper *)context; /* ctx is at offset 0 */

    if (w->init_ok && g_loaded && p_close != NULL)
        p_close(context);

    free(w);
}

/*
 * Free
 * Frees a buffer allocated by this bridge (e.g. the scan-info buffer).
 */
void Free(void *ptr)
{
    free(ptr);
}

/*
 * ScanDevices
 * Opens the EtherCAT master on the NIC and scans for slaves.
 * Returns > 0 on success (NIC opened), 0 on failure.
 * slaveCount is filled with the number of detected slaves (may be 0).
 * scanInfoBuffer receives a small opaque allocation freed by Free().
 *
 * Both ecx_init and ecx_config_init are wrapped in __try/__except so that
 * a native crash inside soem_wrapper.dll (e.g. pcap SEH exception on a bad
 * NIC name or missing Npcap) is caught here and returned as 0 rather than
 * propagating as an unhandled exception to the managed caller.
 */
int ScanDevices(
    ecx_contextt *context,
    const char   *interfaceName,
    void        **scanInfoBuffer,
    int          *slaveCount)
{
    if (scanInfoBuffer != NULL) *scanInfoBuffer = NULL;
    if (slaveCount     != NULL) *slaveCount     = 0;

    if (!g_loaded || context == NULL || p_init == NULL)
        return 0;

    /* Null or empty interface name would crash pcap immediately. */
    if (interfaceName == NULL || interfaceName[0] == '\0')
        return 0;

    /* ecx_init opens the NIC via pcap — guard against SEH crashes. */
    int initOk = 0;
    __try
    {
        initOk = p_init(context, interfaceName);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return 0;
    }

    if (initOk == 0)
        return 0;

    /* Mark init succeeded so FreeContext can safely call ecx_close. */
    ((SoemWrapper *)context)->init_ok = 1;

    /* ecx_config_init scans the bus — guard against SEH crashes. */
    int count = 0;
    if (p_config_init != NULL)
    {
        __try
        {
            count = p_config_init(context);
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            /* Bus scan crashed; treat as zero slaves found. */
            count = 0;
        }
    }

    if (slaveCount != NULL)
        *slaveCount = count;

    if (scanInfoBuffer != NULL)
        *scanInfoBuffer = malloc(1);

    return 1;
}

/*
 * ConfigureIoMap
 * Maps PDOs into ioMap and fills per-slave output/input byte offsets.
 * Indices 1..slaveCount are populated; index 0 is unused.
 * A value of -1 means the slave has no output or input PDO.
 * Returns total IO size in bytes, 0 on failure.
 */
int ConfigureIoMap(
    ecx_contextt *context,
    void         *ioMap,
    int          *outputOffsets,
    int          *inputOffsets,
    int          *totalBytes)
{
    if (totalBytes != NULL) *totalBytes = 0;

    if (!g_loaded || context == NULL || p_config_map == NULL || ioMap == NULL)
        return 0;

    int total = 0;
    __try
    {
        total = p_config_map(context, ioMap, 0);
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        return 0;
    }

    if (total <= 0)
        return 0;

    /* Configure DC sync — required by most CiA 402 drives (CSP/CSV) before OP.
     * Safe no-op on slaves that don't support DC; failures are tolerated so
     * non-DC buses still proceed. */
    if (p_configdc != NULL)
    {
        __try { p_configdc(context); }
        __except (EXCEPTION_EXECUTE_HANDLER) { /* ignore */ }
    }

    if (totalBytes != NULL)
        *totalBytes = total;

    SoemWrapper *wrapper = (SoemWrapper *)context; /* ctx is at offset 0 */
    wrapper->last_wkc = 0;
    wrapper->expected_wkc = expected_wkc(context);

    uint8 *base    = (uint8 *)ioMap;
    int    n       = context->slavecount;
    int    maxSlot = n < (EC_MAXSLAVE - 1) ? n : (EC_MAXSLAVE - 1);

    for (int s = 1; s <= maxSlot; s++)
    {
        ec_slavet *slave = &context->slavelist[s];

        if (outputOffsets != NULL)
        {
            outputOffsets[s] = (slave->outputs != NULL && slave->Obytes > 0)
                ? (int)(slave->outputs - base)
                : -1;
        }

        if (inputOffsets != NULL)
        {
            inputOffsets[s] = (slave->inputs != NULL && slave->Ibytes > 0)
                ? (int)(slave->inputs - base)
                : -1;
        }
    }

    return total;
}

/*
 * ConfigureDcSync0
 * Starts SYNC0 on every DC-capable slave. Returns the number of configured
 * slaves, or -1 if the bridge/SOEM build does not expose ecx_dcsync0.
 */
int ConfigureDcSync0(ecx_contextt *context, uint32 cycleTimeNs, int32 cycleShiftNs)
{
    if (!g_loaded || context == NULL)
        return 0;

    if (p_dcsync0 == NULL)
        return -1;

    int configured = 0;
    int maxSlot = context->slavecount < (EC_MAXSLAVE - 1)
        ? context->slavecount
        : (EC_MAXSLAVE - 1);

    for (int s = 1; s <= maxSlot; s++)
    {
        if (!context->slavelist[s].hasdc)
            continue;

        __try
        {
            p_dcsync0(context, (uint16)s, TRUE, cycleTimeNs, cycleShiftNs);
            configured++;
        }
        __except (EXCEPTION_EXECUTE_HANDLER)
        {
            return configured;
        }
    }

    return configured;
}

/*
 * GetCyclicWkc
 * Returns the last process-data work counter and SOEM's expected WKC.
 */
int GetCyclicWkc(ecx_contextt *context, int *lastWkc, int *expectedWkc)
{
    if (!g_loaded || context == NULL)
        return 0;

    SoemWrapper *wrapper = (SoemWrapper *)context; /* ctx is at offset 0 */
    int expected = expected_wkc(context);
    if (expected > 0)
        wrapper->expected_wkc = expected;

    if (lastWkc != NULL)
        *lastWkc = wrapper->last_wkc;
    if (expectedWkc != NULL)
        *expectedWkc = wrapper->expected_wkc;

    return 1;
}

/*
 * GetProcessIo
 * Returns the raw output and input pointers for a slave (both may be NULL).
 */
void GetProcessIo(
    ecx_contextt *context,
    int           slaveIndex,
    void        **outputPointer,
    void        **inputPointer)
{
    if (outputPointer != NULL) *outputPointer = NULL;
    if (inputPointer  != NULL) *inputPointer  = NULL;

    if (context == NULL || slaveIndex < 1 || slaveIndex >= EC_MAXSLAVE)
        return;

    ec_slavet *slave = &context->slavelist[slaveIndex];
    if (outputPointer != NULL) *outputPointer = slave->outputs;
    if (inputPointer  != NULL) *inputPointer  = slave->inputs;
}

/*
 * GetState
 * Returns the last-read state of a slave (0 = master/broadcast info).
 */
uint16 GetState(ecx_contextt *context, int slaveIndex)
{
    if (context == NULL || slaveIndex < 0 || slaveIndex >= EC_MAXSLAVE)
        return 0;
    return context->slavelist[slaveIndex].state;
}

/*
 * GetAlStatusCode
 * Returns the AL status code of a slave (the EtherCAT-defined reason a slave
 * refused to enter the requested state). 0 means no error.
 * Caller should ReadState() before reading this to ensure freshness.
 */
uint16 GetAlStatusCode(ecx_contextt *context, int slaveIndex)
{
    if (context == NULL || slaveIndex < 0 || slaveIndex >= EC_MAXSLAVE)
        return 0;
    return context->slavelist[slaveIndex].ALstatuscode;
}

/*
 * ReadState
 * Reads the current EtherCAT state of all slaves.
 */
int ReadState(ecx_contextt *context)
{
    if (!g_loaded || context == NULL || p_readstate == NULL)
        return 0;
    return p_readstate(context);
}

/*
 * RequestCommonState
 * Broadcasts a state request to all slaves and waits for them to reach it.
 * Returns 1 on success, 0 if any slave did not reach the target state.
 */
int RequestCommonState(ecx_contextt *context, uint16 requestedState)
{
    if (!g_loaded || context == NULL || p_writestate == NULL || p_statecheck == NULL)
        return 0;

    if (p_readstate != NULL)
        p_readstate(context);

    /* OP transition needs continuous PDO traffic — if the bus stays silent for
     * >SM2 watchdog the slave refuses OP and falls back to SAFE-OP+ERROR with
     * AL=0x001B "sync manager watchdog". eRob/ZeroErr drives often program
     * watchdog windows as tight as 10ms in EEPROM, so the loop must pump frames
     * faster than that. statecheck timeout is dropped to 2ms (was 50ms) so each
     * send/receive cycle takes ~2-4ms — well inside any realistic watchdog. */
    if (requestedState == EC_STATE_OPERATIONAL && p_send != NULL && p_receive != NULL)
    {
        for (int warmup = 0; warmup < 100; warmup++)
        {
            p_send(context);
            int wkc = p_receive(context, EC_TIMEOUTRET3);
            update_wkc(context, wkc);
        }

        context->slavelist[0].state = requestedState;
        int maxSlot = context->slavecount < (EC_MAXSLAVE - 1)
            ? context->slavecount
            : (EC_MAXSLAVE - 1);
        for (int s = 1; s <= maxSlot; s++)
            context->slavelist[s].state = requestedState;
        p_writestate(context, 0);

        for (int chk = 0; chk < 5000; chk++)  /* up to ~10s total (5000 × ~2ms) */
        {
            p_send(context);
            int wkc = p_receive(context, EC_TIMEOUTRET3);
            update_wkc(context, wkc);
            if ((chk % 10) == 0)
            {
                p_statecheck(context, 0, requestedState, 500);

                if ((context->slavelist[0].state & 0x0F) == requestedState)
                    return 1;
            }
        }
        if (p_readstate != NULL)
            p_readstate(context);
        return 0;
    }

    context->slavelist[0].state = requestedState;
    p_writestate(context, 0);

    uint16 actual = p_statecheck(context, 0, requestedState, EC_TIMEOUTSTATE);
    return (actual == requestedState) ? 1 : 0;
}

/*
 * RequestState
 * Requests a state for a single slave (1-based) and waits for it.
 * Returns 1 on success, 0 on timeout or error.
 */
int RequestState(ecx_contextt *context, int slaveIndex, uint16 requestedState)
{
    if (!g_loaded || context == NULL ||
        slaveIndex < 1 || slaveIndex >= EC_MAXSLAVE ||
        p_writestate == NULL || p_statecheck == NULL)
    {
        return 0;
    }

    context->slavelist[slaveIndex].state = requestedState;
    p_writestate(context, (uint16)slaveIndex);

    uint16 actual = p_statecheck(context, (uint16)slaveIndex,
                                 requestedState, EC_TIMEOUTSTATE);
    return (actual == requestedState) ? 1 : 0;
}

/*
 * UpdateIo
 * Sends the current output PDO frame and receives the input PDO frame.
 * Returns the received work counter; dcTime receives context->DCtime.
 */
int UpdateIo(ecx_contextt *context, int64 *dcTime)
{
    if (!g_loaded || context == NULL || p_send == NULL || p_receive == NULL)
        return 0;

    int wkc = 0;
    __try
    {
        p_send(context);
        wkc = p_receive(context, EC_TIMEOUTRET3);
        update_wkc(context, wkc);
        if (dcTime != NULL)
            *dcTime = context->DCtime;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        wkc = -1;
        update_wkc(context, wkc);
    }

    return wkc;
}

/*
 * NoCaSdoRead
 * Reads up to 8 bytes from an SDO object without complete access.
 * valueBuffer must be at least 8 bytes.
 * Returns the work counter (> 0 = success).
 */
int NoCaSdoRead(
    ecx_contextt *context,
    uint16        slave,
    uint16        index,
    uint8         subIndex,
    void         *valueBuffer)
{
    if (!g_loaded || context == NULL || p_sdoread == NULL)
        return 0;

    int size = 8;
    return p_sdoread(context, slave, index, subIndex, FALSE,
                     &size, valueBuffer, EC_TIMEOUTRXM);
}

/*
 * PopLastSdoError
 * Drains context->elist and returns the most recent CoE/mailbox error entry.
 * SOEM returns WKC=0 on SDO abort and pushes the abort reason here, but the
 * managed bridge never inspected it — so every silent abort surfaced as a bare
 * "code=0" with no diagnosis. Call this whenever an SDO call returned WKC<=0
 * (or, defensively, after a "successful" write) to recover the abort code and
 * the offending index/subIndex.
 *
 * Returns 1 if at least one error was drained; 0 if the ring was empty.
 * Out parameters are filled from the *latest* (most recently pushed) error.
 *
 * etype values follow ec_err_type (ec_type.h):
 *   0 = SDO abort (AbortCode is a CiA 301 0x06xxxxxx code)
 *   1 = Emergency object (AbortCode holds vendor errorcode/errorreg packed)
 *   3 = Packet error    4 = SDO info error   9 = Mailbox error
 */
int PopLastSdoError(
    ecx_contextt *context,
    uint16       *slaveOut,
    uint16       *indexOut,
    uint8        *subIndexOut,
    int32        *abortCodeOut,
    int32        *errorTypeOut)
{
    if (slaveOut     != NULL) *slaveOut     = 0;
    if (indexOut     != NULL) *indexOut     = 0;
    if (subIndexOut  != NULL) *subIndexOut  = 0;
    if (abortCodeOut != NULL) *abortCodeOut = 0;
    if (errorTypeOut != NULL) *errorTypeOut = 0;

    if (context == NULL)
        return 0;

    int      found  = 0;
    ec_errort latest = {0};

    /* SOEM's elist is a ring buffer (head=push, tail=pop). Drain everything
     * so stale errors from prior operations don't leak into the next caller. */
    while (context->elist.head != context->elist.tail)
    {
        context->elist.tail = (int16)((context->elist.tail + 1) % (EC_MAXELIST + 1));
        latest = context->elist.Error[context->elist.tail];
        context->elist.Error[context->elist.tail].Signal = FALSE;
        found = 1;
    }

    if (found)
    {
        if (slaveOut     != NULL) *slaveOut     = latest.Slave;
        if (indexOut     != NULL) *indexOut     = latest.Index;
        if (subIndexOut  != NULL) *subIndexOut  = latest.SubIdx;
        if (abortCodeOut != NULL) *abortCodeOut = latest.AbortCode;
        if (errorTypeOut != NULL) *errorTypeOut = (int32)latest.Etype;
        context->ecaterror = FALSE;
    }

    return found;
}

/*
 * ecx_SDOwrite
 * Forwarded directly to soem_wrapper.dll's ecx_SDOwrite.
 * The context pointer is the real ecx_contextt * so this is fully compatible.
 */
#ifndef SOEM_BRIDGE_STATIC
int ecx_SDOwrite(
    ecx_contextt *context,
    uint16        slave,
    uint16        index,
    uint8         subIndex,
    boolean       completeAccess,
    int           psize,
    const void   *p,
    int           timeout)
{
    if (!g_loaded || context == NULL || p_sdowrite == NULL)
        return 0;

    return p_sdowrite(context, slave, index, subIndex,
                      completeAccess, psize, p, timeout);
}
#endif
