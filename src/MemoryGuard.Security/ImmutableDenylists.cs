namespace MemoryGuard.Security;

public static class ImmutableDenylists
{
    /// <summary>
    /// Critical Windows OS processes that must NEVER be terminated or touched.
    /// </summary>
    public static readonly HashSet<string> CriticalSystemProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "System",
        "System Idle Process",
        "Registry",
        "smss",
        "smss.exe",
        "csrss",
        "csrss.exe",
        "wininit",
        "wininit.exe",
        "services",
        "services.exe",
        "lsass",
        "lsass.exe",
        "winlogon",
        "winlogon.exe",
        "svchost",
        "svchost.exe",
        "dwm",
        "dwm.exe",
        "fontdrvhost",
        "fontdrvhost.exe",
        "Memory Compression",
        "Secure System",
        "sihost",
        "sihost.exe",
        "taskhostw",
        "taskhostw.exe",
        "ctfmon",
        "ctfmon.exe",
        "explorer",
        "explorer.exe",
        "SearchHost",
        "SearchHost.exe",
        "StartMenuExperienceHost",
        "StartMenuExperienceHost.exe",
        "ShellExperienceHost",
        "ShellExperienceHost.exe",
        "RuntimeBroker",
        "RuntimeBroker.exe"
    };

    /// <summary>
    /// Security, Antivirus, EDR, and Firewall software that must NEVER be terminated.
    /// </summary>
    public static readonly HashSet<string> SecurityProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "MsMpEng",
        "MsMpEng.exe",
        "NisSrv",
        "NisSrv.exe",
        "SecurityHealthService",
        "SecurityHealthService.exe",
        "SecurityHealthSystray",
        "SecurityHealthSystray.exe",
        "smartscreen",
        "smartscreen.exe",
        "MpCmdRun",
        "MpCmdRun.exe",
        // Popular 3rd party antivirus / EDR
        "bdservicehost",
        "bdservicehost.exe",
        "avp",
        "avp.exe",
        "avguard",
        "avguard.exe",
        "mbam",
        "mbam.exe",
        "mbamservice",
        "mbamservice.exe",
        "ekrn",
        "ekrn.exe",
        "ccSvcHst",
        "ccSvcHst.exe",
        "CSFalconService",
        "CSFalconService.exe",
        "SentinelAgent",
        "SentinelAgent.exe",
        "CylanceSvc",
        "CylanceSvc.exe"
    };

    /// <summary>
    /// Display, GPU, Audio, and Input Device drivers where termination destabilizes display or peripherals.
    /// </summary>
    public static readonly HashSet<string> DriverAndHardwareProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "audiodg",
        "audiodg.exe",
        "nvcontainer",
        "nvcontainer.exe",
        "nvcplui",
        "nvcplui.exe",
        "nvdisplay.container",
        "nvdisplay.container.exe",
        "amdfendrsr",
        "amdfendrsr.exe",
        "RadeonSoftware",
        "RadeonSoftware.exe",
        "igfxCUIService",
        "igfxCUIService.exe",
        "igfxEM",
        "igfxEM.exe",
        "LCore",
        "LCore.exe",
        "LogiOptionsPlus",
        "LogiOptionsPlus.exe",
        "Razer Synapse",
        "Razer Synapse.exe",
        "iCUE",
        "iCUE.exe"
    };

    /// <summary>
    /// Installer and updater processes.
    /// </summary>
    public static readonly HashSet<string> InstallerProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "msiexec",
        "msiexec.exe",
        "trustedinstaller",
        "trustedinstaller.exe",
        "tiworker",
        "tiworker.exe",
        "setup",
        "setup.exe",
        "installer",
        "installer.exe",
        "unins000",
        "unins000.exe"
    };

    /// <summary>
    /// Self identity of this application.
    /// </summary>
    public static readonly HashSet<string> SelfProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "MemoryGuard",
        "MemoryGuard.exe",
        "MemoryGuard.App",
        "MemoryGuard.App.exe"
    };
}
