using System.Runtime.InteropServices;

namespace DumpFinder.Mac;

/// <summary>
/// The few AppKit calls Avalonia does not expose, through the Objective-C runtime: Dock icon visibility
/// (menu-bar-only while the Sentinel runs without a window) and user notifications.
/// Every call is a no-op when a class or selector is missing.
/// </summary>
internal static class MacNative
{
    const string ObjC = "/usr/lib/libobjc.A.dylib";

    [DllImport(ObjC)] static extern IntPtr objc_getClass(string name);
    [DllImport(ObjC)] static extern IntPtr sel_registerName(string name);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr Send(IntPtr receiver, IntPtr selector, long arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern bool SendBool(IntPtr receiver, IntPtr selector, IntPtr arg);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern long SendLong(IntPtr receiver, IntPtr selector);
    [DllImport(ObjC, EntryPoint = "objc_msgSend")] static extern IntPtr SendStr(IntPtr receiver, IntPtr selector, [MarshalAs(UnmanagedType.LPUTF8Str)] string arg);

    static IntPtr Sel(string s) => sel_registerName(s);

    static IntPtr NSString(string s) => SendStr(objc_getClass("NSString"), Sel("stringWithUTF8String:"), s);

    static bool Has(IntPtr cls, string selector) =>
        cls != IntPtr.Zero && SendBool(cls, Sel("instancesRespondToSelector:"), Sel(selector));

    /// <summary>Regular (Dock icon + menu bar) or accessory (menu bar icon only).</summary>
    public static void SetDockIconVisible(bool visible)
    {
        if (!OperatingSystem.IsMacOS()) return;
        try
        {
            var app = Send(objc_getClass("NSApplication"), Sel("sharedApplication"));
            if (app == IntPtr.Zero) return;
            Send(app, Sel("setActivationPolicy:"), visible ? 0L : 1L);   // NSApplicationActivationPolicyRegular / Accessory
            if (visible) Send(app, Sel("activateIgnoringOtherApps:"), 1L);
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException) { }
    }

    /// <summary>
    /// Posts a banner in Notification Center under the app's name; a click brings the app to the front
    /// (the window then shows what the alert was about). False when this macOS refused it.
    /// </summary>
    public static bool Notify(string title, string body)
    {
        if (!OperatingSystem.IsMacOS()) return false;
        try
        {
            var centerClass = objc_getClass("NSUserNotificationCenter");
            var noteClass = objc_getClass("NSUserNotification");
            if (centerClass == IntPtr.Zero || noteClass == IntPtr.Zero) return false;
            var center = Send(centerClass, Sel("defaultUserNotificationCenter"));
            if (center == IntPtr.Zero) return false;   // not running from an app bundle
            var note = Send(Send(noteClass, Sel("alloc")), Sel("init"));
            Send(note, Sel("setTitle:"), NSString(title));
            Send(note, Sel("setInformativeText:"), NSString(body));
            if (Has(noteClass, "setSoundName:")) Send(note, Sel("setSoundName:"), NSString("NSUserNotificationDefaultSoundName"));
            Send(center, Sel("deliverNotification:"), note);
            Send(note, Sel("release"));
            return true;
        }
        catch (Exception e) when (e is EntryPointNotFoundException or DllNotFoundException) { return false; }
    }

    /// <summary>Notifications of this app still shown in Notification Center (test hook).</summary>
    public static long DeliveredCount()
    {
        var center = Send(objc_getClass("NSUserNotificationCenter"), Sel("defaultUserNotificationCenter"));
        return center == IntPtr.Zero ? -1 : SendLong(Send(center, Sel("deliveredNotifications")), Sel("count"));
    }
}
