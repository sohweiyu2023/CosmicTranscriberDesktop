// Explorer-shell relaunch interop adapted from Microsoft Node.js Tools for Visual Studio.
// Copyright (c) Microsoft. Licensed under the Apache License, Version 2.0.

using System.Runtime.InteropServices;
using System.Security;
using System.Security.Principal;
using Microsoft.Win32;

namespace CosmicTranscriber.Utilities;

public static class WindowsSecurity
{
    private const string CompatibilityLayersSubKey =
        @"Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string UserCompatibilityLayersKey =
        @"HKEY_CURRENT_USER\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";
    private const string MachineCompatibilityLayersKey =
        @"HKEY_LOCAL_MACHINE\Software\Microsoft\Windows NT\CurrentVersion\AppCompatFlags\Layers";

    private const int CsidlDesktop = 0;
    private const int ShellWindowClassDesktop = 8;
    private const int ShellWindowFindNeedDispatch = 1;
    private const int ShowNormal = 1;
    private const uint ShellViewObjectBackground = 0;
    private static readonly Guid TopLevelBrowserService =
        new("4C96BE40-915C-11CF-99D3-00AA004AE837");

    public static bool IsProcessElevated()
    {
        try
        {
            using var identity = WindowsIdentity.GetCurrent();
            var principal = new WindowsPrincipal(identity);
            return principal.IsInRole(WindowsBuiltInRole.Administrator);
        }
        catch
        {
            return false;
        }
    }

    public static bool HasRunAsAdminCompatibilityFlag(out string source)
    {
        source = string.Empty;
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                return false;
            }

            foreach (var registryKey in new[] { UserCompatibilityLayersKey, MachineCompatibilityLayersKey })
            {
                if (Registry.GetValue(registryKey, executablePath, null) is string layers
                    && ContainsRunAsAdminToken(layers))
                {
                    source = registryKey.StartsWith("HKEY_CURRENT_USER", StringComparison.Ordinal)
                        ? "the current-user Compatibility setting"
                        : "a machine-wide Compatibility setting";
                    return true;
                }
            }

            var inheritedLayers = Environment.GetEnvironmentVariable("__COMPAT_LAYER");
            if (!string.IsNullOrWhiteSpace(inheritedLayers) && ContainsRunAsAdminToken(inheritedLayers))
            {
                source = "an inherited __COMPAT_LAYER setting";
                return true;
            }
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            StartupDiagnostics.WriteException("ELEVATION COMPATIBILITY CHECK", ex);
        }

        return false;
    }

    public static bool TryClearCurrentUserRunAsAdminCompatibilityFlag(out bool changed, out string error)
    {
        changed = false;
        error = string.Empty;
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath))
            {
                error = "Windows did not provide the current executable path.";
                return false;
            }

            using var key = Registry.CurrentUser.OpenSubKey(CompatibilityLayersSubKey, writable: true);
            if (key?.GetValue(executablePath) is not string layers || !ContainsRunAsAdminToken(layers))
            {
                return true;
            }

            var retainedTokens = layers
                .Split([' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries)
                .Where(token => !string.Equals(token.TrimStart('~'), "RUNASADMIN", StringComparison.OrdinalIgnoreCase))
                .ToList();

            if (retainedTokens.Count == 1 && retainedTokens[0] == "~")
            {
                retainedTokens.Clear();
            }

            if (retainedTokens.Count == 0)
            {
                key.DeleteValue(executablePath, throwOnMissingValue: false);
            }
            else
            {
                key.SetValue(executablePath, string.Join(' ', retainedTokens), RegistryValueKind.String);
            }

            changed = true;
            StartupDiagnostics.Write("ELEVATION", "Removed the current-user RUNASADMIN compatibility token for this executable.");
            return true;
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            error = ex.Message;
            StartupDiagnostics.WriteException("CLEAR ELEVATION COMPATIBILITY", ex);
            return false;
        }
    }

    public static bool TryLaunchCurrentProcessFromExplorer(out string error)
    {
        error = string.Empty;
        try
        {
            var executablePath = Environment.ProcessPath;
            if (string.IsNullOrWhiteSpace(executablePath) || !File.Exists(executablePath))
            {
                error = "Windows did not provide a valid executable path for this application.";
                return false;
            }

            var workingDirectory = Path.GetDirectoryName(executablePath) ?? AppContext.BaseDirectory;

            // Adapted from Microsoft's Execute In Explorer sample and Microsoft Node.js Tools.
            // The desktop Shell dispatch object belongs to Explorer, so Explorer—not this elevated
            // process—performs ShellExecute and supplies its ordinary user integrity level.
            var shellWindows = (IShellWindows)new ShellWindowsClass();
            object desktopLocation = CsidlDesktop;
            object unusedRoot = new();
            var desktopDispatch = shellWindows.FindWindowSW(
                ref desktopLocation,
                ref unusedRoot,
                ShellWindowClassDesktop,
                out _,
                ShellWindowFindNeedDispatch);

            var serviceProvider = (IServiceProvider)desktopDispatch;
            var serviceGuid = TopLevelBrowserService;
            var browserInterfaceGuid = typeof(IShellBrowser).GUID;
            var shellBrowser = (IShellBrowser)serviceProvider.QueryService(
                ref serviceGuid,
                ref browserInterfaceGuid);

            var shellView = shellBrowser.QueryActiveShellView();
            var dispatchGuid = typeof(IDispatch).GUID;
            var folderView = (IShellFolderViewDual)shellView.GetItemObject(
                ShellViewObjectBackground,
                ref dispatchGuid);
            var shellDispatch = (IShellDispatch2)folderView.Application;

            shellDispatch.ShellExecute(
                executablePath,
                string.Empty,
                workingDirectory,
                "open",
                ShowNormal);

            StartupDiagnostics.Write(
                "ELEVATION",
                "Asked the Windows Explorer desktop shell to relaunch the application at the shell privilege level.");
            return true;
        }
        catch (Exception ex) when (ex is COMException
                                   or InvalidCastException
                                   or InvalidOperationException
                                   or IOException
                                   or UnauthorizedAccessException
                                   or PlatformNotSupportedException)
        {
            error = ex.Message;
            StartupDiagnostics.WriteException("UNELEVATED RELAUNCH", ex);
            return false;
        }
    }

    private static bool ContainsRunAsAdminToken(string value)
        => value.Split([' ', '\t', ';'], StringSplitOptions.RemoveEmptyEntries)
            .Any(token => string.Equals(token.TrimStart('~'), "RUNASADMIN", StringComparison.OrdinalIgnoreCase));

    [ComImport]
    [Guid("9BA05972-F6A8-11CF-A442-00A0C90A8F39")]
    [ClassInterface(ClassInterfaceType.None)]
    private class ShellWindowsClass
    {
    }

    [ComImport]
    [Guid("85CB6900-4D95-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IShellWindows
    {
        [return: MarshalAs(UnmanagedType.IDispatch)]
        object FindWindowSW(
            [MarshalAs(UnmanagedType.Struct)] ref object location,
            [MarshalAs(UnmanagedType.Struct)] ref object locationRoot,
            int shellWindowClass,
            out int windowHandle,
            int findOptions);
    }

    [ComImport]
    [Guid("6D5140C1-7436-11CE-8034-00AA006009FA")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IServiceProvider
    {
        [return: MarshalAs(UnmanagedType.Interface)]
        object QueryService(ref Guid serviceGuid, ref Guid interfaceGuid);
    }

    [ComImport]
    [Guid("000214E2-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellBrowser
    {
        void VTableGap01(); // IOleWindow.GetWindow
        void VTableGap02(); // IOleWindow.ContextSensitiveHelp
        void VTableGap03(); // InsertMenusSB
        void VTableGap04(); // SetMenuSB
        void VTableGap05(); // RemoveMenusSB
        void VTableGap06(); // SetStatusTextSB
        void VTableGap07(); // EnableModelessSB
        void VTableGap08(); // TranslateAcceleratorSB
        void VTableGap09(); // BrowseObject
        void VTableGap10(); // GetViewStateStream
        void VTableGap11(); // GetControlWindow
        void VTableGap12(); // SendControlMsg
        IShellView QueryActiveShellView();
    }

    [ComImport]
    [Guid("000214E3-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IShellView
    {
        void VTableGap01(); // IOleWindow.GetWindow
        void VTableGap02(); // IOleWindow.ContextSensitiveHelp
        void VTableGap03(); // TranslateAccelerator
        void VTableGap04(); // EnableModeless
        void VTableGap05(); // UIActivate
        void VTableGap06(); // Refresh
        void VTableGap07(); // CreateViewWindow
        void VTableGap08(); // DestroyViewWindow
        void VTableGap09(); // GetCurrentInfo
        void VTableGap10(); // AddPropertySheetPages
        void VTableGap11(); // SaveViewState
        void VTableGap12(); // SelectItem

        [return: MarshalAs(UnmanagedType.Interface)]
        object GetItemObject(uint aspectOfView, ref Guid interfaceGuid);
    }

    [ComImport]
    [Guid("00020400-0000-0000-C000-000000000046")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IDispatch
    {
    }

    [ComImport]
    [Guid("E7A1AF80-4D96-11CF-960C-0080C7F4EE85")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IShellFolderViewDual
    {
        object Application
        {
            [return: MarshalAs(UnmanagedType.IDispatch)]
            get;
        }
    }

    [ComImport]
    [Guid("A4C6892C-3BA9-11D2-9DEA-00C04FB16162")]
    [InterfaceType(ComInterfaceType.InterfaceIsIDispatch)]
    private interface IShellDispatch2
    {
        void ShellExecute(
            [MarshalAs(UnmanagedType.BStr)] string file,
            [MarshalAs(UnmanagedType.Struct)] object arguments,
            [MarshalAs(UnmanagedType.Struct)] object directory,
            [MarshalAs(UnmanagedType.Struct)] object operation,
            [MarshalAs(UnmanagedType.Struct)] object showCommand);
    }
}
