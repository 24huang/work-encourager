using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Xml;
using System.Windows.Forms;
using System.Xml.Serialization;

public class CommandNote
{
    public string Id = Guid.NewGuid().ToString("N"), Title = "", Command = "", Notes = "";
    public CommandNote Copy() { return (CommandNote)MemberwiseClone(); }
    public override string ToString() { return Title; }
}
public static class CommandNotes
{
    public static readonly string PathName = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "commands.xml");
    public static List<CommandNote> Load()
    {
        if (!File.Exists(PathName)) return new List<CommandNote>();
        using (FileStream stream = File.OpenRead(PathName))
        {
            List<CommandNote> notes = (List<CommandNote>)new XmlSerializer(typeof(List<CommandNote>)).Deserialize(stream);
            Validate(notes); return notes;
        }
    }
    private static void Validate(List<CommandNote> notes)
    {
        if (notes == null) throw new Exception("命令备忘录内容无效。");
        HashSet<string> ids = new HashSet<string>();
        foreach (CommandNote note in notes)
            if (note == null || string.IsNullOrWhiteSpace(note.Id) || !ids.Add(note.Id) || string.IsNullOrWhiteSpace(note.Title) || string.IsNullOrWhiteSpace(note.Command)) throw new Exception("每条命令需要标题、命令正文和唯一编号。");
    }
    public static void Save(List<CommandNote> notes)
    {
        Validate(notes);
        string temporary = PathName + ".pending";
        using (XmlWriter writer = XmlWriter.Create(temporary, new XmlWriterSettings { Indent = true, NewLineHandling = NewLineHandling.Entitize })) new XmlSerializer(typeof(List<CommandNote>)).Serialize(writer, notes);
        // 与工资配置分开保存；写入完成后原子替换，旧文件保留为备份。
        if (File.Exists(PathName)) File.Replace(temporary, PathName, PathName + ".bak"); else File.Move(temporary, PathName);
    }
    public static bool Matches(CommandNote note, string query)
    {
        query = (query ?? "").Trim();
        return (note.Title ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || (note.Command ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0 || (note.Notes ?? "").IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
public sealed class CommandMemoForm : Form
{
    private List<CommandNote> notes;
    private readonly TextBox search, title, command, remarks;
    private readonly ListBox list;
    private readonly Button delete, copy;
    private readonly Label status;
    private string currentId;
    private bool dirty, filling;
    public CommandMemoForm(int theme)
    {
        notes = CommandNotes.Load(); Text = "命令备忘录 · 上班鼓励器";
        Font = new Font("Microsoft YaHei UI", 9); StartPosition = FormStartPosition.CenterScreen;
        ClientSize = new Size(780, 530); MinimumSize = new Size(660, 480);
        Rectangle area = Screen.FromPoint(Cursor.Position).WorkingArea;
        Size = new Size(Math.Min(Width, area.Width), Math.Min(Height, area.Height));
        BackColor = theme == 0 ? Color.FromArgb(18, 23, 43) : Color.FromArgb(252, 241, 249);
        ForeColor = theme == 0 ? Color.FromArgb(223, 232, 250) : Color.FromArgb(86, 55, 91);
        Controls.Add(new Label { Text = "搜索标题、命令或备注", Bounds = new Rectangle(16, 16, 220, 24) });
        search = new TextBox { Name = "CommandSearch", Bounds = new Rectangle(16, 44, 220, 28) }; Controls.Add(search);
        list = new ListBox { Name = "CommandList", Bounds = new Rectangle(16, 84, 220, 374), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left, HorizontalScrollbar = true }; Controls.Add(list);
        Button add = new Button { Name = "NewCommand", Text = "新增", Bounds = new Rectangle(16, 476, 100, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Left }; Controls.Add(add);
        delete = new Button { Name = "DeleteCommand", Text = "删除当前", Bounds = new Rectangle(126, 476, 110, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Left, Enabled = false }; Controls.Add(delete);
        Controls.Add(new Label { Text = "标题", Bounds = new Rectangle(256, 16, 100, 24) });
        title = new TextBox { Name = "CommandTitle", Bounds = new Rectangle(256, 44, 508, 28), Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right }; Controls.Add(title);
        Controls.Add(new Label { Text = "命令（支持多行）", Bounds = new Rectangle(256, 84, 220, 24) });
        command = new TextBox { Name = "CommandBody", Multiline = true, AcceptsReturn = true, AcceptsTab = true, WordWrap = false, ScrollBars = ScrollBars.Both, Font = new Font("Consolas", 10), Bounds = new Rectangle(256, 112, 508, 190), Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right }; Controls.Add(command);
        Controls.Add(new Label { Text = "备注", Bounds = new Rectangle(256, 314, 100, 24), Anchor = AnchorStyles.Bottom | AnchorStyles.Left });
        remarks = new TextBox { Name = "CommandNotes", Multiline = true, ScrollBars = ScrollBars.Vertical, Bounds = new Rectangle(256, 342, 508, 78), Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right }; Controls.Add(remarks);
        status = new Label { Name = "CommandStatus", Text = "保存在本机 · 复制后自行粘贴使用", Bounds = new Rectangle(256, 432, 508, 28), Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right }; Controls.Add(status);
        Button save = new Button { Name = "SaveCommand", Text = "保存 · Ctrl+S", Bounds = new Rectangle(256, 476, 180, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Left }; Controls.Add(save);
        copy = new Button { Name = "CopyCommand", Text = "复制命令", Bounds = new Rectangle(584, 476, 180, 34), Anchor = AnchorStyles.Bottom | AnchorStyles.Right, Enabled = false }; Controls.Add(copy);
        foreach (Control control in Controls) if (control is TextBox || control is ListBox || control is Button)
        { control.BackColor = theme == 0 ? Color.FromArgb(32, 42, 67) : Color.FromArgb(255, 248, 254); control.ForeColor = ForeColor; }
        foreach (TextBox box in new[] { title, command, remarks }) box.TextChanged += delegate { if (!filling) { dirty = true; status.Text = "有未保存修改"; } copy.Enabled = !string.IsNullOrWhiteSpace(command.Text); };
        search.TextChanged += delegate { RefreshList(); };
        list.SelectedIndexChanged += delegate
        {
            if (filling || list.SelectedItem == null) return;
            CommandNote selected = (CommandNote)list.SelectedItem;
            if (selected.Id == currentId) return;
            if (!ConfirmChange()) { RefreshList(); return; }
            Edit(selected);
        };
        add.Click += delegate { if (ConfirmChange()) { search.Clear(); Edit(null); title.Focus(); } };
        save.Click += delegate { SaveNote(); };
        copy.Click += delegate
        {
            try { Clipboard.SetText(command.Text); status.Text = "已复制命令" + (dirty ? "（修改尚未保存）" : ""); }
            catch (Exception ex) { MessageBox.Show(this, "复制失败，请重试。\n" + ex.Message, "命令备忘录"); }
        };
        delete.Click += delegate
        {
            if (currentId == null || MessageBox.Show(this, "删除当前保存的命令“" + title.Text + "”？", "删除命令", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
            try
            {
                List<CommandNote> next = notes.ConvertAll(delegate(CommandNote note) { return note.Copy(); }); next.RemoveAll(delegate(CommandNote note) { return note.Id == currentId; });
                CommandNotes.Save(next); notes = next; Edit(null); RefreshList(); status.Text = "已删除";
            }
            catch (Exception ex) { MessageBox.Show(this, ex.Message, "删除未保存"); }
        };
        FormClosing += delegate(object sender, FormClosingEventArgs e) { if (e.CloseReason == CloseReason.UserClosing && !ConfirmChange()) e.Cancel = true; };
        KeyPreview = true; KeyDown += delegate(object sender, KeyEventArgs e) { if (e.Control && e.KeyCode == Keys.S) { e.SuppressKeyPress = true; SaveNote(); } };
        RefreshList();
    }
    private bool ConfirmChange()
    {
        if (!dirty) return true;
        DialogResult answer = MessageBox.Show(this, "是否保存当前命令的修改？", "命令备忘录", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
        if (answer == DialogResult.Yes) return SaveNote();
        if (answer == DialogResult.No) { dirty = false; return true; }
        return false;
    }
    private void Edit(CommandNote note)
    {
        filling = true; currentId = note == null ? null : note.Id;
        title.Text = note == null ? "" : note.Title; command.Text = note == null ? "" : note.Command; remarks.Text = note == null ? "" : note.Notes;
        dirty = false; delete.Enabled = note != null; filling = false; RefreshList(); status.Text = note == null ? "填写标题与命令，保存后可再次使用" : "已加载 · 可编辑或复制";
    }
    private void RefreshList()
    {
        filling = true; list.BeginUpdate(); list.Items.Clear();
        foreach (CommandNote note in notes) if (CommandNotes.Matches(note, search.Text)) { list.Items.Add(note); if (note.Id == currentId) list.SelectedItem = note; }
        list.EndUpdate(); filling = false;
    }
    private bool SaveNote()
    {
        try
        {
            CommandNote saved = new CommandNote { Id = currentId ?? Guid.NewGuid().ToString("N"), Title = title.Text.Trim(), Command = command.Text, Notes = remarks.Text };
            List<CommandNote> next = notes.ConvertAll(delegate(CommandNote note) { return note.Copy(); });
            int index = next.FindIndex(delegate(CommandNote note) { return note.Id == saved.Id; }); if (index < 0) next.Add(saved); else next[index] = saved;
            CommandNotes.Save(next); notes = next; Edit(saved); status.Text = "已保存到本机"; return true;
        }
        catch (Exception ex) { MessageBox.Show(this, ex.Message, "命令未保存"); return false; }
    }
}
