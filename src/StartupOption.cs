using System;
using System.Windows.Forms;
using Microsoft.Win32;

// 开机启动属于当前电脑，不写入可携带的个人配置。启用时只注册当前程序路径。
public static class StartupOption
{
    private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
    public const string ValueName = "WorkEncourager";
    public static string CommandFor(string executable)
    {
        if (string.IsNullOrEmpty(executable) || executable.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0 || executable.Length > 240)
            throw new Exception("程序路径无效或过长，无法设置开机启动。");
        return "\"" + executable + "\" --startup";
    }
    public static string CurrentCommand
    {
        get { using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKey)) return key == null ? null : key.GetValue(ValueName) as string; }
    }
    public static bool Enabled { get { return !string.IsNullOrEmpty(CurrentCommand); } }
    public static bool UsesCurrentPath { get { return string.Equals(CurrentCommand, "\"" + Application.ExecutablePath + "\" --startup", StringComparison.OrdinalIgnoreCase); } }
    public static void Write(RegistryKey key, bool enabled, string executable)
    {
        if (enabled) key.SetValue(ValueName, CommandFor(executable), RegistryValueKind.String);
        else key.DeleteValue(ValueName, false);
    }
    public static void Apply(bool enabled, string restoreCommand = null)
    {
        using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKey))
        { if (enabled && restoreCommand != null) key.SetValue(ValueName, restoreCommand, RegistryValueKind.String); else Write(key, enabled, Application.ExecutablePath); }
    }
}
