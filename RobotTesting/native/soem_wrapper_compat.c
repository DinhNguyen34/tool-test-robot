#define WIN32_LEAN_AND_MEAN
#include <windows.h>
#include <strsafe.h>
#include <stdint.h>
#include <string.h>
#include <wchar.h>

#include "soem/soem.h"

#ifndef ARRAYSIZE
#define ARRAYSIZE(a) (sizeof(a) / sizeof((a)[0]))
#endif

static HMODULE g_selfModule = NULL;
static HMODULE g_wrapperModule = NULL;
static ecx_contextt g_context;
static int g_contextPrepared = 0;
static int g_sessionOpen = 0;

ec_slavet ec_slave[EC_MAXSLAVE];
int ec_slavecount = 0;
ec_groupt ec_group[EC_MAXGROUP];
int64 ec_DCtime = 0;

typedef ec_adaptert *(__cdecl *PFN_ec_find_adapters)(void);
typedef void (__cdecl *PFN_ec_free_adapters)(ec_adaptert *adapter);
typedef int (__cdecl *PFN_ecx_init)(ecx_contextt *context, const char *ifname);
typedef int (__cdecl *PFN_ecx_config_init)(ecx_contextt *context);
typedef int (__cdecl *PFN_ecx_config_map_group)(ecx_contextt *context, void *pIOmap, uint8 group);
typedef boolean (__cdecl *PFN_ecx_configdc)(ecx_contextt *context);
typedef void (__cdecl *PFN_ecx_close)(ecx_contextt *context);
typedef int (__cdecl *PFN_ecx_readstate)(ecx_contextt *context);
typedef int (__cdecl *PFN_ecx_writestate)(ecx_contextt *context, uint16 slave);
typedef uint16 (__cdecl *PFN_ecx_statecheck)(ecx_contextt *context, uint16 slave, uint16 reqstate, int timeout);
typedef int (__cdecl *PFN_ecx_send_processdata)(ecx_contextt *context);
typedef int (__cdecl *PFN_ecx_receive_processdata)(ecx_contextt *context, int timeout);
typedef int (__cdecl *PFN_ecx_SDOread)(ecx_contextt *context, uint16 slave, uint16 index, uint8 subindex,
    boolean CA, int *psize, void *p, int timeout);
typedef int (__cdecl *PFN_ecx_SDOwrite)(ecx_contextt *context, uint16 slave, uint16 index, uint8 subindex,
    boolean CA, int psize, const void *p, int timeout);
typedef void *(__cdecl *PFN_osal_mutex_create)(void);
typedef void (__cdecl *PFN_osal_mutex_destroy)(void *mutex);

static PFN_ec_find_adapters p_ec_find_adapters = NULL;
static PFN_ec_free_adapters p_ec_free_adapters = NULL;
static PFN_ecx_init p_ecx_init = NULL;
static PFN_ecx_config_init p_ecx_config_init = NULL;
static PFN_ecx_config_map_group p_ecx_config_map_group = NULL;
static PFN_ecx_configdc p_ecx_configdc = NULL;
static PFN_ecx_close p_ecx_close = NULL;
static PFN_ecx_readstate p_ecx_readstate = NULL;
static PFN_ecx_writestate p_ecx_writestate = NULL;
static PFN_ecx_statecheck p_ecx_statecheck = NULL;
static PFN_ecx_send_processdata p_ecx_send_processdata = NULL;
static PFN_ecx_receive_processdata p_ecx_receive_processdata = NULL;
static PFN_ecx_SDOread p_ecx_SDOread = NULL;
static PFN_ecx_SDOwrite p_ecx_SDOwrite = NULL;
/* Optional — soem_wrapper.dll may not export these; we fall back to CRITICAL_SECTION. */
static PFN_osal_mutex_create p_osal_mutex_create = NULL;
static PFN_osal_mutex_destroy p_osal_mutex_destroy = NULL;

/* ── Mutex fallback using Windows CRITICAL_SECTION ─────────────────────── */

static void *compat_mutex_create(void)
{
    if (p_osal_mutex_create != NULL)
        return p_osal_mutex_create();

    CRITICAL_SECTION *cs = (CRITICAL_SECTION *)HeapAlloc(GetProcessHeap(), 0, sizeof(CRITICAL_SECTION));
    if (cs != NULL)
        InitializeCriticalSection(cs);
    return cs;
}

static void compat_mutex_destroy(void *mutex)
{
    if (mutex == NULL)
        return;

    if (p_osal_mutex_destroy != NULL)
    {
        p_osal_mutex_destroy(mutex);
        return;
    }

    DeleteCriticalSection((CRITICAL_SECTION *)mutex);
    HeapFree(GetProcessHeap(), 0, mutex);
}

/* ─────────────────────────────────────────────────────────────────────── */

static FARPROC compat_get_export(const char *name)
{
    if (g_wrapperModule == NULL)
    {
        return NULL;
    }

    return GetProcAddress(g_wrapperModule, name);
}

static int compat_resolve_exports(void)
{
    p_ec_find_adapters = (PFN_ec_find_adapters)compat_get_export("ec_find_adapters");
    p_ec_free_adapters = (PFN_ec_free_adapters)compat_get_export("ec_free_adapters");
    p_ecx_init = (PFN_ecx_init)compat_get_export("ecx_init");
    p_ecx_config_init = (PFN_ecx_config_init)compat_get_export("ecx_config_init");
    p_ecx_config_map_group = (PFN_ecx_config_map_group)compat_get_export("ecx_config_map_group");
    p_ecx_configdc = (PFN_ecx_configdc)compat_get_export("ecx_configdc");
    p_ecx_close = (PFN_ecx_close)compat_get_export("ecx_close");
    p_ecx_readstate = (PFN_ecx_readstate)compat_get_export("ecx_readstate");
    p_ecx_writestate = (PFN_ecx_writestate)compat_get_export("ecx_writestate");
    p_ecx_statecheck = (PFN_ecx_statecheck)compat_get_export("ecx_statecheck");
    p_ecx_send_processdata = (PFN_ecx_send_processdata)compat_get_export("ecx_send_processdata");
    p_ecx_receive_processdata = (PFN_ecx_receive_processdata)compat_get_export("ecx_receive_processdata");
    p_ecx_SDOread = (PFN_ecx_SDOread)compat_get_export("ecx_SDOread");
    p_ecx_SDOwrite = (PFN_ecx_SDOwrite)compat_get_export("ecx_SDOwrite");

    /* Optional — fall back to compat_mutex_create/destroy if absent. */
    p_osal_mutex_create = (PFN_osal_mutex_create)compat_get_export("osal_mutex_create");
    p_osal_mutex_destroy = (PFN_osal_mutex_destroy)compat_get_export("osal_mutex_destroy");

    return p_ec_find_adapters != NULL &&
        p_ec_free_adapters != NULL &&
        p_ecx_init != NULL &&
        p_ecx_config_init != NULL &&
        p_ecx_config_map_group != NULL &&
        p_ecx_configdc != NULL &&
        p_ecx_close != NULL &&
        p_ecx_readstate != NULL &&
        p_ecx_writestate != NULL &&
        p_ecx_statecheck != NULL &&
        p_ecx_send_processdata != NULL &&
        p_ecx_receive_processdata != NULL &&
        p_ecx_SDOread != NULL &&
        p_ecx_SDOwrite != NULL;
}

static int compat_ensure_wrapper_loaded(void)
{
    if (g_wrapperModule != NULL)
    {
        return 1;
    }

    wchar_t selfPath[MAX_PATH];
    wchar_t wrapperPath[MAX_PATH];

    if (g_selfModule != NULL &&
        GetModuleFileNameW(g_selfModule, selfPath, ARRAYSIZE(selfPath)) > 0)
    {
        wchar_t *lastSlash = wcsrchr(selfPath, L'\\');
        if (lastSlash != NULL)
        {
            *(lastSlash + 1) = L'\0';
            if (SUCCEEDED(StringCchCopyW(wrapperPath, ARRAYSIZE(wrapperPath), selfPath)) &&
                SUCCEEDED(StringCchCatW(wrapperPath, ARRAYSIZE(wrapperPath), L"soem_wrapper.dll")))
            {
                g_wrapperModule = LoadLibraryW(wrapperPath);
            }
        }
    }

    if (g_wrapperModule == NULL)
    {
        g_wrapperModule = LoadLibraryW(L"soem_wrapper.dll");
    }

    if (g_wrapperModule == NULL)
    {
        return 0;
    }

    if (!compat_resolve_exports())
    {
        FreeLibrary(g_wrapperModule);
        g_wrapperModule = NULL;
        return 0;
    }

    return 1;
}

static void compat_sync_from_context(void)
{
    memcpy(ec_slave, g_context.slavelist, sizeof(ec_slave));
    memcpy(ec_group, g_context.grouplist, sizeof(ec_group));
    ec_slavecount = g_context.slavecount;
    ec_DCtime = g_context.DCtime;
}

static void compat_reset_exports(void)
{
    memset(ec_slave, 0, sizeof(ec_slave));
    memset(ec_group, 0, sizeof(ec_group));
    ec_slavecount = 0;
    ec_DCtime = 0;
}

static void compat_discard_context(void)
{
    memset(&g_context, 0, sizeof(g_context));
    g_contextPrepared = 0;
    g_sessionOpen = 0;
    compat_reset_exports();
}

static void compat_cleanup_context(void)
{
    int group;

    for (group = 0; group < EC_MAXGROUP; ++group)
    {
        if (g_context.grouplist[group].mbxtxqueue.mbxmutex != NULL)
        {
            compat_mutex_destroy(g_context.grouplist[group].mbxtxqueue.mbxmutex);
            g_context.grouplist[group].mbxtxqueue.mbxmutex = NULL;
        }
    }

    if (g_context.mbxpool.mbxmutex != NULL)
    {
        compat_mutex_destroy(g_context.mbxpool.mbxmutex);
        g_context.mbxpool.mbxmutex = NULL;
    }

    compat_discard_context();
}

static int compat_prepare_context(void)
{
    int group;
    int slot;

    if (!compat_ensure_wrapper_loaded())
    {
        return 0;
    }

    if (g_sessionOpen)
    {
        p_ecx_close(&g_context);
        compat_discard_context();
    }

    compat_cleanup_context();
    memset(&g_context, 0, sizeof(g_context));

    for (group = 0; group < EC_MAXGROUP; ++group)
    {
        g_context.grouplist[group].logstartaddr = ((uint32)group) << EC_LOGGROUPOFFSET;
        g_context.grouplist[group].mbxtxqueue.mbxmutex = (osal_mutext *)compat_mutex_create();
        g_context.grouplist[group].mbxtxqueue.listhead = 0;
        g_context.grouplist[group].mbxtxqueue.listtail = 0;
        g_context.grouplist[group].mbxtxqueue.listcount = 0;
        for (slot = 0; slot < EC_MBXPOOLSIZE; ++slot)
        {
            g_context.grouplist[group].mbxtxqueue.mbxticket[slot] = -1;
        }
    }

    g_context.mbxpool.mbxmutex = (osal_mutext *)compat_mutex_create();

    g_contextPrepared = 1;
    compat_reset_exports();
    return 1;
}

static void compat_copy_requested_state(uint16 slave)
{
    if (slave == 0)
    {
        g_context.slavelist[0].state = ec_slave[0].state;
        return;
    }

    if (slave < EC_MAXSLAVE)
    {
        g_context.slavelist[slave].state = ec_slave[slave].state;
    }
}

BOOL WINAPI DllMain(HINSTANCE instance, DWORD reason, LPVOID reserved)
{
    (void)reserved;

    if (reason == DLL_PROCESS_ATTACH)
    {
        g_selfModule = instance;
        DisableThreadLibraryCalls(instance);
    }
    else if (reason == DLL_PROCESS_DETACH)
    {
        if (g_sessionOpen && p_ecx_close != NULL)
        {
            p_ecx_close(&g_context);
            compat_discard_context();
        }
        else
        {
            compat_cleanup_context();
        }

        if (g_wrapperModule != NULL)
        {
            FreeLibrary(g_wrapperModule);
            g_wrapperModule = NULL;
        }
    }

    return TRUE;
}

int ec_init(const char *ifname)
{
    int result;

    if (!compat_prepare_context())
    {
        return 0;
    }

    result = p_ecx_init(&g_context, ifname);
    if (result > 0)
    {
        g_sessionOpen = 1;
        compat_sync_from_context();
        return result;
    }

    compat_cleanup_context();
    return result;
}

void ec_close(void)
{
    if (g_sessionOpen && p_ecx_close != NULL)
    {
        p_ecx_close(&g_context);
        compat_discard_context();
        return;
    }

    compat_cleanup_context();
}

int ec_config_init(uint8 usetable)
{
    int result;
    (void)usetable;

    if (!g_contextPrepared || p_ecx_config_init == NULL)
    {
        return 0;
    }

    result = p_ecx_config_init(&g_context);
    compat_sync_from_context();
    return result;
}

int ec_config_map(void *pIOmap)
{
    int result;

    if (!g_contextPrepared || p_ecx_config_map_group == NULL)
    {
        return 0;
    }

    result = p_ecx_config_map_group(&g_context, pIOmap, 0);
    compat_sync_from_context();
    return result;
}

boolean ec_configdc(void)
{
    boolean result;

    if (!g_contextPrepared || p_ecx_configdc == NULL)
    {
        return FALSE;
    }

    result = p_ecx_configdc(&g_context);
    compat_sync_from_context();
    return result;
}

ec_adaptert *ec_find_adapters(void)
{
    if (!compat_ensure_wrapper_loaded() || p_ec_find_adapters == NULL)
    {
        return NULL;
    }

    return p_ec_find_adapters();
}

void ec_free_adapters(ec_adaptert *adapter)
{
    if (!compat_ensure_wrapper_loaded() || p_ec_free_adapters == NULL)
    {
        return;
    }

    p_ec_free_adapters(adapter);
}

int ec_readstate(void)
{
    int result;

    if (!g_contextPrepared || p_ecx_readstate == NULL)
    {
        return 0;
    }

    result = p_ecx_readstate(&g_context);
    compat_sync_from_context();
    return result;
}

int ec_writestate(uint16 slave)
{
    int result;

    if (!g_contextPrepared || p_ecx_writestate == NULL)
    {
        return 0;
    }

    compat_copy_requested_state(slave);
    result = p_ecx_writestate(&g_context, slave);
    compat_sync_from_context();
    return result;
}

uint16 ec_statecheck(uint16 slave, uint16 reqstate, int timeout)
{
    uint16 result;

    if (!g_contextPrepared || p_ecx_statecheck == NULL)
    {
        return 0;
    }

    compat_copy_requested_state(slave);
    result = p_ecx_statecheck(&g_context, slave, reqstate, timeout);
    compat_sync_from_context();
    return result;
}

int ec_send_processdata(void)
{
    if (!g_contextPrepared || p_ecx_send_processdata == NULL)
    {
        return 0;
    }

    return p_ecx_send_processdata(&g_context);
}

int ec_receive_processdata(int timeout)
{
    int result;

    if (!g_contextPrepared || p_ecx_receive_processdata == NULL)
    {
        return 0;
    }

    result = p_ecx_receive_processdata(&g_context, timeout);
    compat_sync_from_context();
    return result;
}

int ec_SDOread(uint16 slave, uint16 index, uint8 subindex,
    boolean completeAccess, int *psize, void *p, int timeout)
{
    if (!g_contextPrepared || p_ecx_SDOread == NULL)
    {
        return 0;
    }

    return p_ecx_SDOread(&g_context, slave, index, subindex, completeAccess, psize, p, timeout);
}

int ec_SDOwrite(uint16 slave, uint16 index, uint8 subindex,
    boolean completeAccess, int psize, const void *p, int timeout)
{
    if (!g_contextPrepared || p_ecx_SDOwrite == NULL)
    {
        return 0;
    }

    return p_ecx_SDOwrite(&g_context, slave, index, subindex, completeAccess, psize, p, timeout);
}
