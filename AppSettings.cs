using System;
using System.IO;
using System.Linq;
using System.Drawing;
using System.Windows.Forms;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using System.Reflection;

[assembly: AssemblyVersion("1.1.0.0")]
[assembly: AssemblyFileVersion("1.1.0.0")]

namespace ComfyDrop {
public static class ProfileStore {
    public static string DirectoryPath { get { return Environment.GetEnvironmentVariable("COMFYDROP_PROFILE_DIR") ?? Path.Combine(Environment.GetEnvironmentVariable("LOCALAPPDATA") ?? Path.Combine(Environment.GetEnvironmentVariable("USERPROFILE"),"AppData","Local"),"ComfyDrop"); } }
    public static void Migrate(string legacyDirectory){
        Directory.CreateDirectory(DirectoryPath);
        foreach(string name in new[]{"settings.json","network-profile.json"}){
            string target=Path.Combine(DirectoryPath,name),source=Path.Combine(legacyDirectory,name);
            if(!File.Exists(target)&&File.Exists(source))File.Copy(source,target,false);
        }
    }
    public static void Write(string path,string text){
        Directory.CreateDirectory(Path.GetDirectoryName(path));string temp=path+".new";
        File.WriteAllText(temp,text);if(File.Exists(path))File.Replace(temp,path,null);else File.Move(temp,path);
    }
}
public partial class MainWindow {
    void ShowSettings(){if(busy)return;using(var dialog=CreateSettingsDialog())dialog.ShowDialog(this);}
    Form CreateSettingsDialog(){
        var dialog=new Form{Text="ComfyDrop settings · version 1.1",Size=new Size(750,570),MinimumSize=new Size(700,530),StartPosition=FormStartPosition.CenterParent,Font=Font,BackColor=BackColor,MinimizeBox=false,MaximizeBox=false,ShowInTaskbar=false};
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(18),ColumnCount=1,RowCount=3};layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,65));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,42));dialog.Controls.Add(layout);
        var tabs=new TabControl{Dock=DockStyle.Fill};layout.Controls.Add(tabs);
        var copies=new Dictionary<TextBox,TextBox>();
        Func<string,TableLayoutPanel> page=title=>{var tab=new TabPage(title){Padding=new Padding(14),BackColor=Color.White};tabs.TabPages.Add(tab);var panel=new TableLayoutPanel{Dock=DockStyle.Top,ColumnCount=3,AutoSize=true};panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,145));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent,100));panel.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute,100));tab.Controls.Add(panel);return panel;};
        Action<TableLayoutPanel,string,TextBox,bool,string> field=(panel,label,original,secret,picker)=>{int row=panel.RowCount++;panel.RowStyles.Add(new RowStyle(SizeType.Absolute,40));var box=new TextBox{Text=original.Text,Dock=DockStyle.Fill,UseSystemPasswordChar=secret};copies.Add(original,box);panel.Controls.Add(new Label{Text=label,AutoSize=true,Margin=new Padding(0,6,0,0)},0,row);panel.Controls.Add(box,1,row);if(picker==null)panel.SetColumnSpan(box,2);else {var choose=Button("Choose…",delegate {if(picker=="folder"){using(var d=new FolderBrowserDialog()){if(Directory.Exists(box.Text))d.SelectedPath=box.Text;if(d.ShowDialog(dialog)==DialogResult.OK)box.Text=d.SelectedPath;}}else PickFile(box,picker);});choose.Width=94;panel.Controls.Add(choose,2,row);}};
        var network=page("Network drive");field(network,"Datacenter",regionBox,false,null);field(network,"Volume ID",volumeBox,false,null);field(network,"S3 access key",accessBox,false,null);field(network,"S3 secret",secretBox,true,null);
        var rememberCopy=new CheckBox{Text="Remember credentials on this Windows account",Checked=remember.Checked,AutoSize=true};int rr=network.RowCount++;network.Controls.Add(rememberCopy,1,rr);network.SetColumnSpan(rememberCopy,2);
        var guide=Button("Setup guide",delegate {MessageBox.Show(dialog,"Open RunPod Storage and select your network volume to find its volume ID and datacenter.\n\nIn RunPod Credentials > S3 API Keys, create an S3 key and paste its access key and secret here. This is separate from a regular RunPod API key.\n\nYour datacenter must support RunPod's S3 API. The folder path is relative to /workspace; for example ComfyUI/output/.","Network drive setup");});network.Controls.Add(guide,1,network.RowCount++);
        var general=page("Downloads & updates");field(general,"Save downloads to",dest,false,"folder");var note=new Label{Text="Files go directly into this folder, preserving subfolders.\nNetwork downloads keep both files when a name already exists: image (2).png.\n\nSettings and encrypted credentials stay in your Windows profile across updates.",AutoSize=true,MaximumSize=new Size(590,0),Margin=new Padding(0,12,0,12)};general.Controls.Add(note,0,general.RowCount++);general.SetColumnSpan(note,3);
        var update=Button("Install update…",delegate{InstallUpdate(dialog);});update.Width=155;general.Controls.Add(update,1,general.RowCount++);
        var advanced=page("runpodctl / SSH");field(advanced,"runpodctl location",cli,false,"Executable|*.exe");field(advanced,"SSH host / IP",host,false,null);field(advanced,"SSH user",user,false,null);field(advanced,"SSH port",port,false,null);field(advanced,"SSH private key",key,false,"All files|*.*");
        var location=new Label{Text="Saved settings: "+ProfileStore.DirectoryPath+"\nUpdates keep these files. S3 secrets are encrypted for your Windows account.",Dock=DockStyle.Fill,AutoEllipsis=true};layout.Controls.Add(location);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};var close=Button("Cancel",delegate{dialog.DialogResult=DialogResult.Cancel;});actions.Controls.Add(close);dialog.CancelButton=close;
        var save=Button("Save settings",delegate{try{if(!Path.IsPathRooted(copies[dest].Text))throw new Exception("Choose an absolute download folder.");foreach(var pair in copies)pair.Key.Text=pair.Value.Text;remember.Checked=rememberCopy.Checked;Save();SaveNetwork();status.Text="Settings saved. Load a folder to browse your files.";dialog.DialogResult=DialogResult.OK;}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"Could not save settings");}});actions.Controls.Add(save);dialog.AcceptButton=save;layout.Controls.Add(actions);return dialog;
    }
    void InstallUpdate(Form dialog){
        string script=Path.Combine(AppDomain.CurrentDomain.BaseDirectory,"Update.ps1");if(!File.Exists(script)){MessageBox.Show(dialog,"Extract the new ComfyDrop ZIP and run Update.cmd from it.","Install update");return;}
        using(var pick=new OpenFileDialog{Filter="ComfyDrop update ZIP|*.zip",Title="Choose the new ComfyDrop ZIP"}){if(pick.ShowDialog(dialog)!=DialogResult.OK)return;
            if(MessageBox.Show(dialog,"Close ComfyDrop and install this update? Unsaved edits in Settings will be discarded. Saved settings and credentials will be kept.","Install update",MessageBoxButtons.OKCancel,MessageBoxIcon.Information)!=DialogResult.OK)return;
            try{var args=new[]{"-NoProfile","-ExecutionPolicy","Bypass","-STA","-File",script,"-Package",pick.FileName,"-Destination",AppDomain.CurrentDomain.BaseDirectory.TrimEnd('\\'),"-WaitForProcess",Process.GetCurrentProcess().Id.ToString()};Process.Start(new ProcessStartInfo("powershell.exe",String.Join(" ",args.Select(Arg).ToArray())){UseShellExecute=false,CreateNoWindow=true});dialog.Close();Close();}catch(Exception ex){MessageBox.Show(dialog,ex.Message,"Could not start updater");}
        }
    }
    bool ConfirmRunpodDestination(string folder){return !Directory.EnumerateFileSystemEntries(folder).Any()||MessageBox.Show(this,"runpodctl may replace files with matching names in:\n"+folder+"\n\nContinue receiving here?","Receive files",MessageBoxButtons.YesNo,MessageBoxIcon.Warning,MessageBoxDefaultButton.Button2)==DialogResult.Yes;}
    public static string AvailableDownloadPath(string target,HashSet<string> reserved){
        string candidate=target;int suffix=2;while(File.Exists(candidate)||Directory.Exists(candidate)||reserved.Contains(candidate)){candidate=Path.Combine(Path.GetDirectoryName(target),Path.GetFileNameWithoutExtension(target)+" ("+(suffix++)+")"+Path.GetExtension(target));}reserved.Add(candidate);return candidate;
    }
    public static void ValidateDeleteKey(string key){if(String.IsNullOrEmpty(key)||key.StartsWith("/")||key.IndexOf('\\')>=0||key.Any(c=>Char.IsControl(c))||key.TrimEnd('/').Split('/').Any(p=>p.Length==0||p=="."||p==".."))throw new IOException("Unsafe or unsupported deletion path: "+key);}
    public static List<ObjectInfo> DeletePlan(List<ObjectInfo> selected,List<ObjectInfo> expanded){
        foreach(var item in selected)ValidateDeleteKey(item.Key);
        foreach(var item in expanded){ValidateDeleteKey(item.Key);if(!selected.Any(s=>item.Key==s.Key||(s.Folder&&s.Key.EndsWith("/")&&item.Key.StartsWith(s.Key,StringComparison.Ordinal))))throw new IOException("The drive returned a file outside the selected folders. Nothing was deleted.");}
        var all=expanded.Concat(selected).ToList();
        foreach(var item in expanded){for(int i=item.Key.IndexOf('/');i>=0;i=item.Key.IndexOf('/',i+1)){string parent=item.Key.Substring(0,i+1);if(selected.Any(s=>s.Folder&&parent.StartsWith(s.Key,StringComparison.Ordinal)))all.Add(new ObjectInfo{Key=parent,Folder=true});}}
        return all.GroupBy(x=>x.Key).Select(g=>g.First()).OrderBy(x=>x.Folder).ThenByDescending(x=>x.Key.Count(c=>c=='/')).ThenBy(x=>x.Key,StringComparer.Ordinal).ToList();
    }
    bool ConfirmDeletion(List<ObjectInfo> plan,string volume){using(var dialog=new Form{Text="Delete from network drive?",Size=new Size(700,510),StartPosition=FormStartPosition.CenterParent,Font=Font,MinimizeBox=false,MaximizeBox=false,ShowInTaskbar=false}){
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,Padding=new Padding(16),ColumnCount=1,RowCount=3};layout.RowStyles.Add(new RowStyle(SizeType.Absolute,80));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,45));dialog.Controls.Add(layout);
        layout.Controls.Add(new Label{Text="Permanently delete "+plan.Count(x=>!x.Folder)+" files and "+plan.Count(x=>x.Folder)+" folders from volume "+volume+"?\nFolder contents are included. This cannot be undone. Local downloads are kept.",Dock=DockStyle.Fill});
        layout.Controls.Add(new TextBox{Text=String.Join(Environment.NewLine,plan.Select(x=>x.Key).ToArray()),Multiline=true,ReadOnly=true,ScrollBars=ScrollBars.Both,WordWrap=false,Dock=DockStyle.Fill});
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill,FlowDirection=FlowDirection.RightToLeft};var no=Button("Cancel",delegate{dialog.DialogResult=DialogResult.Cancel;});actions.Controls.Add(no);dialog.CancelButton=no;dialog.AcceptButton=no;var yes=Button("Delete permanently",delegate{dialog.DialogResult=DialogResult.Yes;});yes.Width=180;yes.ForeColor=Color.Firebrick;actions.Controls.Add(yes);layout.Controls.Add(actions);return dialog.ShowDialog(this)==DialogResult.Yes;
    }}
    async Task DeleteNetwork(){
        if(busy)return;networkCancel=new CancellationTokenSource();cancelled=false;int removed=0;bool attempted=false;
        NetworkBusy(true,"Checking selected files and folder contents…");
        try{var drive=Drive();string prefix=Prefix();if(networkIdentity!=Identity()||prefix!=networkPrefix)throw new Exception("Load this folder before deleting.");var selected=networkFiles.SelectedItems.Cast<ListViewItem>().Select(i=>(ObjectInfo)i.Tag).ToList();if(selected.Count==0)throw new Exception("Select the files or folders you want to delete.");foreach(var item in selected){ValidateDeleteKey(item.Key);if(!item.Key.StartsWith(prefix,StringComparison.Ordinal)||item.Key==prefix)throw new IOException("Invalid selection. Reload the folder.");}
            var expanded=new List<ObjectInfo>();foreach(var item in selected)if(item.Folder)expanded.AddRange(await drive.List(item.Key,true,networkCancel.Token));var plan=DeletePlan(selected,expanded);networkCancel.Token.ThrowIfCancellationRequested();if(!ConfirmDeletion(plan,volumeBox.Text)) {status.Text="Deletion cancelled. Nothing was deleted.";return;}
            foreach(var item in plan){networkCancel.Token.ThrowIfCancellationRequested();status.Text="Deleting "+(removed+1)+" / "+plan.Count+" · "+item.Key;attempted=true;await drive.Delete(item.Key,networkCancel.Token);removed++;Log("Deleted "+item.Key);}status.Text="Deleted "+removed+" items from the network drive.";
        }catch(OperationCanceledException){status.Text="Deletion stopped. "+removed+" items confirmed deleted; reload to check the remaining files.";Log(status.Text);}catch(Exception ex){Error(new IOException(ex.Message+"\n"+removed+" items confirmed deleted. Reload the folder to check remaining files."));}
        finally{if(attempted){networkEntries.Clear();RenderNetwork();}networkCancel.Dispose();networkCancel=null;NetworkBusy(false,status.Text);}
        if(attempted){string summary=status.Text;await BrowseNetwork();status.Text=summary;}
    }
}
}
