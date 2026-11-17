using System;
using System.Collections.Generic;
using System.IO;
using System.Windows.Forms;
using System.Runtime.InteropServices;

public static class CommandMemoTests
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern IntPtr FindWindow(string className, string title);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr window, out uint process);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr window, uint message, IntPtr parameter, IntPtr data);
    public static void Run(Action<bool, string> check)
    {
        if (File.Exists(CommandNotes.PathName)) File.Delete(CommandNotes.PathName);
        check(CommandNotes.Load().Count == 0, "Command memo starts empty without creating a personal data file");
        string text = "git status\r\ngit log --oneline -5\r\n";
        List<CommandNote> notes = new List<CommandNote> { new CommandNote { Title = "查看代码状态", Command = text, Notes = "发布前核对" } };
        CommandNotes.Save(notes); List<CommandNote> loaded = CommandNotes.Load();
        check(loaded.Count == 1 && loaded[0].Command == text && loaded[0].Notes == notes[0].Notes, "Multiline commands and trailing line breaks survive a local reload exactly");
        check(CommandNotes.Matches(loaded[0], "STATUS") && CommandNotes.Matches(loaded[0], "代码") && CommandNotes.Matches(loaded[0], "发布"), "Search matches title, command and notes without case sensitivity");
        CommandNote copy = loaded[0].Copy(); copy.Command = "changed";
        check(loaded[0].Command == text, "Editing a copied note leaves its saved counterpart intact");
        string original = File.ReadAllText(CommandNotes.PathName); bool rejected = false;
        try { CommandNotes.Save(new List<CommandNote> { new CommandNote { Title = "Empty" } }); } catch { rejected = true; }
        check(rejected && File.ReadAllText(CommandNotes.PathName) == original, "Invalid memo input cannot overwrite previously saved commands");
        loaded[0].Notes = "已更新"; CommandNotes.Save(loaded);
        check(File.Exists(CommandNotes.PathName + ".bak") && CommandNotes.Load()[0].Notes == "已更新", "Editing atomically saves a new version and retains a local backup");
        using (CommandMemoForm form = new CommandMemoForm(0))
        {
            form.Opacity = 0; form.Show(); Application.DoEvents();
            ((ListBox)form.Controls["CommandList"]).SelectedIndex = 0;
            check(form.Controls["CommandBody"].Text == text && form.Controls["CopyCommand"].Enabled, "Selecting a saved command populates its full text and enables copying");
            form.Controls["CommandNotes"].Text = "界面编辑"; ((Button)form.Controls["SaveCommand"]).PerformClick();
            check(CommandNotes.Load().Count == 1 && CommandNotes.Load()[0].Notes == "界面编辑", "Saving an edit updates the same record instead of duplicating it");
            ((Button)form.Controls["NewCommand"]).PerformClick();
            form.Controls["CommandTitle"].Text = "查看分支"; form.Controls["CommandBody"].Text = "git branch";
            ((Button)form.Controls["SaveCommand"]).PerformClick();
            check(CommandNotes.Load().Count == 2, "The real memo form adds a second command");
            form.Controls["CommandSearch"].Text = "分支";
            check(((ListBox)form.Controls["CommandList"]).Items.Count == 1, "The real memo form filters command titles");
            // 只响应本测试进程的原生提示框，不触碰用户其他窗口或剪贴板。
            using (Timer answer = new Timer { Interval = 25 })
            {
                int choice = 2;
                answer.Tick += delegate
                {
                    IntPtr dialog = FindWindow("#32770", "命令备忘录"); uint process;
                    if (dialog != IntPtr.Zero && GetWindowThreadProcessId(dialog, out process) != 0 && process == System.Diagnostics.Process.GetCurrentProcess().Id)
                    { PostMessage(dialog, 0x111, new IntPtr(choice), IntPtr.Zero); answer.Stop(); }
                };
                form.Controls["CommandNotes"].Text = "关闭前未保存的修改";
                answer.Start(); form.Close();
                check(!form.IsDisposed && CommandNotes.Load()[1].Notes != "关闭前未保存的修改", "Canceling the close prompt keeps the unsaved editor open without changing disk content");
                choice = 6; answer.Start(); form.Close();
                check(form.IsDisposed && CommandNotes.Load()[1].Notes == "关闭前未保存的修改", "Choosing save on close persists the draft before closing the memo");
            }
        }
        CommandNotes.Save(new List<CommandNote>()); check(CommandNotes.Load().Count == 0, "Deleting the final record persists an empty memo");
        File.WriteAllText(CommandNotes.PathName, "<broken>"); rejected = false;
        try { CommandNotes.Load(); } catch { rejected = true; }
        check(rejected && File.ReadAllText(CommandNotes.PathName) == "<broken>", "A damaged memo is reported without silently replacing personal content");
        File.Delete(CommandNotes.PathName);
    }
}
