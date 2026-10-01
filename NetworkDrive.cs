using System;
using System.IO;
using System.Net;
using System.Text;
using System.Linq;
using System.Xml.Linq;
using System.Security.Cryptography;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using System.Drawing;
using System.Windows.Forms;

namespace ComfyDrop {
public class DriveProfile { public string Region="", Volume="", Access="", Secret="", Prefix="ComfyUI/output/"; }
public class ObjectInfo { public string Key; public long Size; public string Modified; public bool Folder; }
public class S3Drive {
    readonly string region,bucket,access,secret; readonly Uri endpoint;
    public S3Drive(string region,string bucket,string access,string secret,Uri endpoint){ServicePointManager.SecurityProtocol=SecurityProtocolType.Tls12;this.region=region;this.bucket=bucket;this.access=access;this.secret=secret;this.endpoint=endpoint;}
    public static string Encode(string value){return Uri.EscapeDataString(value);}
    static string Hex(byte[] value){return BitConverter.ToString(value).Replace("-","").ToLowerInvariant();}
    static byte[] Hash(string value){using(var sha=SHA256.Create())return sha.ComputeHash(Encoding.UTF8.GetBytes(value));}
    static byte[] Hmac(byte[] key,string value){using(var h=new HMACSHA256(key))return h.ComputeHash(Encoding.UTF8.GetBytes(value));}
    public static string Signature(string secret,string date,string region,string value){byte[] k=Hmac(Encoding.UTF8.GetBytes("AWS4"+secret),date);k=Hmac(k,region);k=Hmac(k,"s3");k=Hmac(k,"aws4_request");return Hex(Hmac(k,value));}
    HttpWebRequest Request(string key,SortedDictionary<string,string> query,string method){
        string path="/"+Encode(bucket)+(key==null?"":"/"+String.Join("/",key.Split('/').Select(Encode).ToArray()));
        string qs=String.Join("&",query.Select(p=>Encode(p.Key)+"="+Encode(p.Value)).ToArray());
        var uri=new Uri(endpoint.GetLeftPart(UriPartial.Authority)+path+(qs.Length>0?"?"+qs:""));
        var request=(HttpWebRequest)WebRequest.Create(uri); request.Method=method;request.AllowAutoRedirect=false;request.Timeout=120000;request.ReadWriteTimeout=120000;
        string time=DateTime.UtcNow.ToString("yyyyMMddTHHmmssZ"),date=time.Substring(0,8),payload=Hex(Hash(""));
        string headers="host:"+uri.Authority+"\nx-amz-content-sha256:"+payload+"\nx-amz-date:"+time+"\n";
        string signed="host;x-amz-content-sha256;x-amz-date";
        string canonical=method+"\n"+path+"\n"+qs+"\n"+headers+"\n"+signed+"\n"+payload;
        string scope=date+"/"+region+"/s3/aws4_request";
        string value="AWS4-HMAC-SHA256\n"+time+"\n"+scope+"\n"+Hex(Hash(canonical));
        request.Headers["x-amz-date"]=time;request.Headers["x-amz-content-sha256"]=payload;
        request.Headers["Authorization"]="AWS4-HMAC-SHA256 Credential="+access+"/"+scope+", SignedHeaders="+signed+", Signature="+Signature(secret,date,region,value);
        return request;
    }
    async Task<T> Get<T>(string key,SortedDictionary<string,string> query,Func<HttpWebResponse,Task<T>> consume,CancellationToken token,string method="GET"){
        for(int attempt=0;;attempt++){
            if(attempt>0)await Task.Delay(1000*(1<<(attempt-1)),token);
            token.ThrowIfCancellationRequested();var request=Request(key,query,method);
            try{using(token.Register(request.Abort))using(var response=(HttpWebResponse)await request.GetResponseAsync()){if((int)response.StatusCode<200||(int)response.StatusCode>=300)throw new IOException("Unexpected network drive response: HTTP "+(int)response.StatusCode);return await consume(response);}}
            catch(WebException ex){token.ThrowIfCancellationRequested();var response=ex.Response as HttpWebResponse;int status=response==null?0:(int)response.StatusCode;string detail="";if(response!=null){using(response)using(var reader=new StreamReader(response.GetResponseStream()))detail=reader.ReadToEnd();}if(method=="GET"&&attempt<3&&(status==0||status==429||status>=500)){continue;}string message=status==403?"Access denied. Check your S3 access key, secret, and datacenter.":status==404?"Volume or file not found. Check your volume ID and datacenter.":"Network drive request failed (HTTP "+status+").";try{var x=XDocument.Parse(detail);var m=x.Descendants().FirstOrDefault(e=>e.Name.LocalName=="Message");if(m!=null)message+=" "+m.Value;}catch{}throw new IOException(message,ex);}
        }
    }
    public async Task<List<ObjectInfo>> List(string prefix,bool recursive,CancellationToken token){
        var result=new List<ObjectInfo>();var tokens=new HashSet<string>();string next="";
        do {var query=new SortedDictionary<string,string>(StringComparer.Ordinal){{"list-type","2"},{"max-keys","1000"},{"prefix",prefix}};if(!recursive)query.Add("delimiter","/");if(next.Length>0)query.Add("continuation-token",next);
            string xml=await Get<string>(null,query,async r=>{using(var sr=new StreamReader(r.GetResponseStream()))return await sr.ReadToEndAsync();},token);
            var document=XDocument.Parse(xml);var root=document.Root;Func<XElement,string,string> val=(e,n)=>{var c=e.Elements().FirstOrDefault(a=>a.Name.LocalName==n);return c==null?"":c.Value;};
            foreach(var e in root.Elements()){
                if(e.Name.LocalName=="Contents"){string k=val(e,"Key");long size;Int64.TryParse(val(e,"Size"),out size);if(k!=prefix)result.Add(new ObjectInfo{Key=k,Size=size,Modified=val(e,"LastModified"),Folder=k.EndsWith("/")});}
                if(e.Name.LocalName=="CommonPrefixes"){string p=val(e,"Prefix");if(p!=prefix)result.Add(new ObjectInfo{Key=p,Folder=true,Modified=""});}
            }
            bool more=val(root,"IsTruncated")=="true";next=more?val(root,"NextContinuationToken"):"";
            if(more&&(next.Length==0||!tokens.Add(next)))throw new IOException("RunPod returned a repeated or missing page token. Try loading a smaller subfolder, or try again shortly.");
        }while(next.Length>0);
        return result.GroupBy(x=>x.Key).Select(g=>g.First()).ToList();
    }
    public async Task Delete(string key,CancellationToken token){MainWindow.ValidateDeleteKey(key);await Get<bool>(key,new SortedDictionary<string,string>(),response=>Task.FromResult(true),token,"DELETE");}
    public async Task Download(ObjectInfo item,string target,Action<long,long> progress,CancellationToken token){
        string temp=target+"."+Guid.NewGuid().ToString("N")+".part";Directory.CreateDirectory(Path.GetDirectoryName(target));
        try {await Get<bool>(item.Key,new SortedDictionary<string,string>(),async response=>{
            using(var source=response.GetResponseStream())using(var output=new FileStream(temp,FileMode.Create,FileAccess.Write,FileShare.None,131072,true)){
                byte[] buffer=new byte[131072];long count=0;int read;var watch=System.Diagnostics.Stopwatch.StartNew();while((read=await source.ReadAsync(buffer,0,buffer.Length,token))>0){await output.WriteAsync(buffer,0,read,token);count+=read;if(watch.ElapsedMilliseconds>150){progress(count,response.ContentLength);watch.Restart();}}if(response.ContentLength>=0&&count!=response.ContentLength)throw new IOException("Incomplete download.");progress(count,count);
            }return true;},token);token.ThrowIfCancellationRequested();File.Move(temp,target);
        }finally{if(File.Exists(temp))File.Delete(temp);}
    }
    public static string LocalPath(string root,string relative){
        var parts=relative.Split('/');var safe=new List<string>();foreach(string part in parts){if(part=="."||part==".."||part.Length==0)throw new IOException("Unsupported object path.");var s=new StringBuilder();foreach(char c in part){if(c=='%'||Array.IndexOf(Path.GetInvalidFileNameChars(),c)>=0)s.Append("%"+((int)c).ToString("X4"));else s.Append(c);}string name=s.ToString();while(name.EndsWith(".")||name.EndsWith(" "))name=name.Substring(0,name.Length-1)+"%"+((int)name[name.Length-1]).ToString("X4");if(System.Text.RegularExpressions.Regex.IsMatch(name,@"^(CON|PRN|AUX|NUL|COM[1-9]|LPT[1-9])(?:\.|$)",System.Text.RegularExpressions.RegexOptions.IgnoreCase))name="%005F"+name;safe.Add(name);}
        string full=Path.GetFullPath(Path.Combine(root,Path.Combine(safe.ToArray())));if(!full.StartsWith(Path.GetFullPath(root).TrimEnd('\\')+"\\",StringComparison.OrdinalIgnoreCase))throw new IOException("Unsafe object path.");return full;
    }
}

public partial class MainWindow {
    TextBox regionBox=new TextBox(),volumeBox=new TextBox(),accessBox=new TextBox(),secretBox=new TextBox(),prefixBox=new TextBox(),networkFilter=new TextBox();
    ListView networkFiles=new ListView();Button networkLoad,networkDownload,networkAll,networkDelete;CheckBox remember=new CheckBox();
    CancellationTokenSource networkCancel; List<ObjectInfo> networkEntries=new List<ObjectInfo>();string networkPrefix="",networkIdentity="";
    string ProfilePath {get{return Path.Combine(ProfileStore.DirectoryPath,"network-profile.json");}}
    void AddNetworkTab(TabControl tabs){
        var tab=new TabPage("Network drive · works with pod stopped") {Padding=new Padding(12),BackColor=Color.White};var previous=tabs.TabPages.Cast<TabPage>().ToArray();tabs.TabPages.Clear();tabs.TabPages.Add(tab);tabs.TabPages.AddRange(previous);tabs.SelectedTab=tab;
        var layout=new TableLayoutPanel{Dock=DockStyle.Fill,ColumnCount=1,RowCount=4};tab.Controls.Add(layout);
        layout.RowStyles.Add(new RowStyle(SizeType.Absolute,40));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,34));layout.RowStyles.Add(new RowStyle(SizeType.Percent,100));layout.RowStyles.Add(new RowStyle(SizeType.Absolute,44));
        var folder=Row();folder.Controls.Add(new Label{Text="Folder in drive",AutoSize=true},0,0);prefixBox.Text="ComfyUI/output/";prefixBox.Dock=DockStyle.Fill;folder.Controls.Add(prefixBox,1,0);networkLoad=Button("Load folder",async delegate{await BrowseNetwork();});folder.Controls.Add(networkLoad,2,0);layout.Controls.Add(folder);
        var search=new FlowLayoutPanel{Dock=DockStyle.Fill};search.Controls.Add(new Label{Text="Search this folder",AutoSize=true,Margin=new Padding(0,6,8,0)});networkFilter.Width=240;networkFilter.TextChanged+=delegate{RenderNetwork();};search.Controls.Add(networkFilter);search.Controls.Add(new Label{Text="Double-click folders to open · Ctrl / Shift for multiple files",AutoSize=true,Margin=new Padding(10,6,0,0)});layout.Controls.Add(search);
        networkFiles.View=View.Details;networkFiles.FullRowSelect=true;networkFiles.MultiSelect=true;networkFiles.HideSelection=false;networkFiles.Dock=DockStyle.Fill;networkFiles.Columns.Add("Name",425);networkFiles.Columns.Add("Size",110);networkFiles.Columns.Add("Modified",210);networkFiles.DoubleClick+=async delegate{if(!busy&&networkFiles.SelectedItems.Count==1){var item=(ObjectInfo)networkFiles.SelectedItems[0].Tag;if(item.Folder){prefixBox.Text=item.Key;await BrowseNetwork();}}};layout.Controls.Add(networkFiles);
        var actions=new FlowLayoutPanel{Dock=DockStyle.Fill};actions.Controls.Add(Button("Drive root",async delegate{if(!busy){prefixBox.Text="";await BrowseNetwork();}}));actions.Controls.Add(Button("Parent folder",async delegate{if(!busy){string p=prefixBox.Text.TrimEnd('/');int i=p.LastIndexOf('/');prefixBox.Text=i<0?"":p.Substring(0,i+1);await BrowseNetwork();}}));networkDownload=Button("Download selected",async delegate{await DownloadNetwork(false);});networkDownload.Width=165;actions.Controls.Add(networkDownload);networkAll=Button("Download whole folder",async delegate{await DownloadNetwork(true);});networkAll.Width=190;actions.Controls.Add(networkAll);networkDelete=Button("Delete selected…",async delegate{await DeleteNetwork();});networkDelete.Width=150;networkDelete.ForeColor=Color.Firebrick;actions.Controls.Add(networkDelete);layout.Controls.Add(actions);
        try{if(File.Exists(ProfilePath)){var p=json.Deserialize<DriveProfile>(File.ReadAllText(ProfilePath));regionBox.Text=p.Region;volumeBox.Text=p.Volume;prefixBox.Text=p.Prefix;if(p.Secret.Length>0){accessBox.Text=Unprotect(p.Access);secretBox.Text=Unprotect(p.Secret);remember.Checked=true;}}}catch(Exception ex){Log("Saved network profile could not be loaded: "+ex.Message);}
    }
    static string Protect(string s){return Convert.ToBase64String(ProtectedData.Protect(Encoding.UTF8.GetBytes(s),null,DataProtectionScope.CurrentUser));}
    static string Unprotect(string s){return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(s),null,DataProtectionScope.CurrentUser));}
    void SaveNetwork(){ProfileStore.Write(ProfilePath,json.Serialize(new DriveProfile{Region=regionBox.Text.Trim(),Volume=volumeBox.Text.Trim(),Prefix=prefixBox.Text,Access=remember.Checked?Protect(accessBox.Text.Trim()):"",Secret=remember.Checked?Protect(secretBox.Text.Trim()):""}));}
    S3Drive Drive(){string r=regionBox.Text.Trim().ToUpperInvariant(),v=volumeBox.Text.Trim();if(!System.Text.RegularExpressions.Regex.IsMatch(r,@"^[A-Z]{2,4}-[A-Z0-9]+-\d+$")||!System.Text.RegularExpressions.Regex.IsMatch(v,@"^[a-zA-Z0-9-]+$"))throw new Exception("Enter the datacenter (for example EUR-IS-1) and volume ID shown in RunPod Storage.");if(String.IsNullOrWhiteSpace(accessBox.Text)||String.IsNullOrWhiteSpace(secretBox.Text))throw new Exception("Enter your RunPod S3 access key and secret. The Setup guide explains where to find them.");return new S3Drive(r,v,accessBox.Text.Trim(),secretBox.Text.Trim(),new Uri("https://s3api-"+r.ToLowerInvariant()+".runpod.io"));}
    string Prefix(){string p=prefixBox.Text.Trim().TrimStart('/');if(p.StartsWith("workspace/"))p=p.Substring(10);if(p.Length>0&&!p.EndsWith("/"))p+="/";prefixBox.Text=p;return p;}
    string Identity(){return regionBox.Text.Trim()+"|"+volumeBox.Text.Trim();}
    void NetworkBusy(bool value,string text){SetBusy(value,text);networkLoad.Enabled=networkDownload.Enabled=networkAll.Enabled=networkDelete.Enabled=!value;regionBox.Enabled=volumeBox.Enabled=accessBox.Enabled=secretBox.Enabled=prefixBox.Enabled=!value;}
    async Task BrowseNetwork(){if(busy)return;cancelled=false;networkCancel=new CancellationTokenSource();NetworkBusy(true,"Loading network drive…");try{var drive=Drive();string prefix=Prefix();SaveNetwork();networkEntries=await drive.List(prefix,false,networkCancel.Token);networkPrefix=prefix;networkIdentity=Identity();RenderNetwork();status.Text=networkEntries.Count==0?"Folder is empty or not found. Try Drive root to locate your ComfyUI folder.":networkEntries.Count+" items · double-click folders, or select files to download.";}catch(Exception ex){Error(ex);}finally{networkCancel.Dispose();networkCancel=null;NetworkBusy(false,status.Text);}}
    void RenderNetwork(){networkFiles.BeginUpdate();networkFiles.Items.Clear();foreach(var e in networkEntries.OrderByDescending(x=>x.Folder).ThenBy(x=>x.Key,StringComparer.OrdinalIgnoreCase)){string name=e.Key.StartsWith(networkPrefix)?e.Key.Substring(networkPrefix.Length):e.Key;if(name.IndexOf(networkFilter.Text,StringComparison.OrdinalIgnoreCase)<0)continue;var row=new ListViewItem(name);row.SubItems.Add(e.Folder?"Folder":SizeText(e.Size));row.SubItems.Add(e.Modified);row.Tag=e;networkFiles.Items.Add(row);}networkFiles.EndUpdate();}
    async Task DownloadNetwork(bool whole){if(busy)return;cancelled=false;networkCancel=new CancellationTokenSource();NetworkBusy(true,"Preparing download…");try{var drive=Drive();string prefix=Prefix();if(networkIdentity!=Identity()||prefix!=networkPrefix)throw new Exception("Load this folder before downloading.");var selected=networkFiles.SelectedItems.Cast<ListViewItem>().Select(i=>(ObjectInfo)i.Tag).ToList();if(!whole&&selected.Count==0)throw new Exception("Select files or folders to download first.");var items=new List<ObjectInfo>();if(whole)items=await drive.List(prefix,true,networkCancel.Token);else foreach(var item in selected){if(item.Folder)items.AddRange(await drive.List(item.Key,true,networkCancel.Token));else items.Add(item);}items=items.Where(i=>!i.Folder).GroupBy(i=>i.Key).Select(g=>g.First()).ToList();if(items.Count==0)throw new Exception("No files in this selection.");string destination=NewDestination();var names=new HashSet<string>(StringComparer.OrdinalIgnoreCase);int done=0;foreach(var item in items){networkCancel.Token.ThrowIfCancellationRequested();if(!item.Key.StartsWith(prefix,StringComparison.Ordinal))throw new IOException("Unexpected file outside the selected folder.");string target=S3Drive.LocalPath(destination,item.Key.Substring(prefix.Length));target=AvailableDownloadPath(target,names);Log("Downloading "+item.Key);await drive.Download(item,target,(count,total)=>{status.Text=(done+1)+" / "+items.Count+" · "+Path.GetFileName(target)+" · "+SizeText(count)+(total>0?" / "+SizeText(total):"");if(total>0){progress.Style=ProgressBarStyle.Blocks;progress.Value=(int)Math.Min(100,count*100.0/total);}},networkCancel.Token);done++;}status.Text="Downloaded "+done+" files. Click Open downloads to view them.";Log(status.Text);}catch(OperationCanceledException){status.Text="Download cancelled. Completed files are kept; incomplete files are removed.";Log(status.Text);}catch(Exception ex){Error(ex);}finally{networkCancel.Dispose();networkCancel=null;NetworkBusy(false,status.Text);}}
}
}
