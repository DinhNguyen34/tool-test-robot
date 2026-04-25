/*
 * soem_bridge.c
 *
 * Implements the context-handle API expected by SoemNative.cs.
 * Loads soem_wrapper.dll at runtime (ecx_* context API).
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
} SoemWrapper;

/* ── DLL handles ──────────────────────────────────────────────────────────── */
static HMODULE g_self    = NULL;
static HMODULE g_wrapper = NULL;
static int     g_loaded  = 0;

/* ── Function pointer types ───────────────────────────────────────────────── */
typedef int     (__cdecl *PFN_ecx_init)(ecx_contextt *, const char *);
typedef int     (__cdecl *PFN_ecx_config_init)(ecx_contextt *);
typedef int     (__cdecl *PFN_ecx_config_map_group)(ecx_contextt *, void *, uint8);
typedef boolean (__cdecl *PFN_ecx_configdc)(ecx_contextt *);
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

static PFN_ecx_init             p_init        = NULL;
static PFN_ecx_config_init      p_config_init = NULL;
static PFN_ecx_config_map_group p_config_map  = NULL;
static PFN_ecx_configdc         p_configdc    = NULL;
static PFN_ecx_close            p_close       = NULL;
static PFN_ecx_readstate        p_readstate   = NULL;
static PFN_ecx_writestate       p_writestate  = NULL;
static PFN_ecx_statecheck       p_statecheck  = NULL;
static PFN_ecx_send             p_send        = NULL;
static PFN_ecx_receive          p_receive     = NULL;
static PFN_ecx_SDOread          p_sdoread     = NULL;
static PFN_ecx_SDOwrite         p_sdowrite    = NULL;

/* ── Loader ───────────────────────────────────────────────────────────────── */

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
        if (g_wrapper != NULL)
        {
            FreeLibrary(g_wrapper);
            g_wrapper = NULL;
        }
    }
    return TRUE;
}

/* ── Public API ───────────────────────────────────────────────────────────── */

/*
 * CreateContext
 * Allocates and zero-initialises a SoemWrapper.
 * Returns &wrapper->ctx cast to ecx_contextt * (same address, compatible pointer).
 */
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

    if (totalBytes != NULL)
        *totalBytes = total;

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
        if (dcTime != NULL)
            *dcTime = context->DCtime;
    }
    __except (EXCEPTION_EXECUTE_HANDLER)
    {
        wkc = -1;
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
 * ecx_SDOwrite
 * Forwarded directly to soem_wrapper.dll's ecx_SDOwrite.
 * The context pointer is the real ecx_contextt * so this is fully compatible.
 */
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
