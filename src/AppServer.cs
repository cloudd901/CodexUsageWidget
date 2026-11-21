using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Web.Script.Serialization;

namespace CodexUsageBubble {
 internal sealed class RpcException : Exception {
  internal readonly bool Busy;
  internal readonly bool OwnedByAnotherClient;
  internal readonly bool MissingThread;
  internal RpcException(string message) : base(message) {
   var text=message.ToLowerInvariant();
   OwnedByAnotherClient=text.Contains("active writer")||text.Contains("open in another")||text.Contains("already has a writer");
   Busy=OwnedByAnotherClient||text.Contains("already running");
   MissingThread=text.Contains("thread not found")||text.Contains("no thread found")||text.Contains("no codex thread found for threadid:")||text.Contains("could not find thread")||System.Text.RegularExpressions.Regex.IsMatch(text,@"^thread\s+\S+\s+(?:was\s+)?not found\b");
  }
 }
 internal sealed class AttentionException : Exception {
  internal AttentionException(string message) : base(message) { }
 }
 // One protocol reader per child. Notifications received before a response are retained.
 internal sealed class AppServer : IDisposable {
  readonly Process process=new Process();
  readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=16*1024*1024};
  readonly Queue<Dictionary<string,object>> events=new Queue<Dictionary<string,object>>();
  readonly CancellationToken token;
  CancellationTokenRegistration registration;
  ProcessJob job;
  StreamWriter input;
  int nextId;
  internal AppServer(string executable,CancellationToken cancel) {
   token=cancel;token.ThrowIfCancellationRequested();
   process.StartInfo=new ProcessStartInfo(executable,"app-server") {UseShellExecute=false,CreateNoWindow=true,RedirectStandardInput=true,RedirectStandardOutput=true,RedirectStandardError=true,WorkingDirectory=Path.GetDirectoryName(executable),StandardOutputEncoding=System.Text.Encoding.UTF8,StandardErrorEncoding=System.Text.Encoding.UTF8};
   try {
    process.Start();job=new ProcessJob(process);input=new StreamWriter(process.StandardInput.BaseStream,new System.Text.UTF8Encoding(false)) {AutoFlush=true};
    process.ErrorDataReceived+=delegate {};process.BeginErrorReadLine();
    registration=token.Register(Kill);
    Request("initialize",new {clientInfo=new {name="codex_usage_bubble",version="1.1.0"},capabilities=new {experimentalApi=true}});
    Send(new {method="initialized"});
   }catch {Dispose();throw;}
  }
  internal static object Get(Dictionary<string,object> value,string key) {object result;return value!=null&&value.TryGetValue(key,out result)?result:null;}
  internal static string Text(Dictionary<string,object> value,string key) {return Get(value,key) as string;}
  internal Dictionary<string,object> Request(string method,object parameters) {
   int id=++nextId;Send(new {id=id,method=method,@params=parameters});
   while(true) {
    var message=Read();
    if(Get(message,"method")!=null) {HandleServerRequest(message);events.Enqueue(message);continue;}
    if(!object.Equals(Get(message,"id"),id))continue;
    var error=Get(message,"error") as Dictionary<string,object>;
    if(error!=null)throw new RpcException(Text(error,"message")??"Codex rejected the request.");
    return Get(message,"result") as Dictionary<string,object>??new Dictionary<string,object>();
   }
  }
  internal Dictionary<string,object> NextEvent() {
   var message=events.Count>0?events.Dequeue():Read();HandleServerRequest(message);return message;
  }
  void HandleServerRequest(Dictionary<string,object> message) {
   if(Get(message,"id")==null||Get(message,"method")==null)return;
   // This small client has no approval/tool UI. Never accept requests on the user's behalf.
   Send(new {id=message["id"],error=new {code=-32601,message="This background client cannot handle interactive requests. Continue in the Codex app."}});
   throw new AttentionException("Needs attention: this chat requested a tool, approval, or answer that requires an interactive Codex client.");
  }
  Dictionary<string,object> Read() {
   token.ThrowIfCancellationRequested();
   try {
    var read=process.StandardOutput.ReadLineAsync();read.Wait(token);
    token.ThrowIfCancellationRequested();
    var line=read.GetAwaiter().GetResult();
    if(line==null)throw new IOException("Codex closed its background connection.");
    return json.Deserialize<Dictionary<string,object>>(line);
   }catch {token.ThrowIfCancellationRequested();throw;}
  }
  void Send(object message) {token.ThrowIfCancellationRequested();input.WriteLine(json.Serialize(message));}
  internal void Interrupt(string thread,string turn) {
   try {if(!token.IsCancellationRequested)Send(new {id=++nextId,method="turn/interrupt",@params=new {threadId=thread,turnId=turn}});}catch(Exception){}
  }
  void Kill() {try{if(job!=null)job.Terminate();if(!process.HasExited)process.Kill();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){} }
  public void Dispose() {
   registration.Dispose();
   try {if(input!=null)input.Close();else process.StandardInput.Close();if(!process.WaitForExit(300))Kill();process.WaitForExit(2000);}catch(InvalidOperationException){}catch(IOException){Kill();}
   finally {if(job!=null)job.Dispose();process.Dispose();}
  }
 }
 // Kill descendants as well as the app server, including if the widget exits unexpectedly.
 internal sealed class ProcessJob : IDisposable {
  IntPtr handle;
  [StructLayout(LayoutKind.Sequential)] struct Basic {public long PerProcess,PerJob;public uint Flags;public UIntPtr Minimum,Maximum;public uint Active;public UIntPtr Affinity;public uint Priority,Scheduling;}
  [StructLayout(LayoutKind.Sequential)] struct Io {public ulong ReadOps,WriteOps,OtherOps,ReadBytes,WriteBytes,OtherBytes;}
  [StructLayout(LayoutKind.Sequential)] struct Limits {public Basic Basic;public Io Io;public UIntPtr ProcessMemory,JobMemory,PeakProcess,PeakJob;}
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern IntPtr CreateJobObject(IntPtr attributes,string name);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool SetInformationJobObject(IntPtr job,int kind,ref Limits info,uint length);
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool AssignProcessToJobObject(IntPtr job,IntPtr process);
  [DllImport("kernel32.dll")] static extern bool TerminateJobObject(IntPtr job,uint code);
  [DllImport("kernel32.dll")] static extern bool CloseHandle(IntPtr handle);
  internal ProcessJob(Process process) {
   handle=CreateJobObject(IntPtr.Zero,null);var limits=new Limits();limits.Basic.Flags=0x2000;
   if(handle==IntPtr.Zero||!SetInformationJobObject(handle,9,ref limits,(uint)Marshal.SizeOf(typeof(Limits)))||!AssignProcessToJobObject(handle,process.Handle)) {
    Dispose();throw new IOException("Could not contain the Codex background process for safe cleanup.");
   }
  }
  internal void Terminate(){if(handle!=IntPtr.Zero)TerminateJobObject(handle,1);}
  public void Dispose(){if(handle!=IntPtr.Zero){CloseHandle(handle);handle=IntPtr.Zero;}}
 }
}
