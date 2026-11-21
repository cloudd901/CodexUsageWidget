using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using Microsoft.Win32.SafeHandles;

namespace CodexUsageBubble {
 internal interface IDesktopAppClient : IDisposable {
  Dictionary<string,object> Request(string method,object parameters);
 }
 // The length-prefixed JSON-RPC transport used by Codex's bundled app tools.
 internal sealed class DesktopAppClient : IDesktopAppClient {
  internal const int MaxFrameBytes=8*1024*1024;
  readonly NamedPipeClientStream pipe;
  readonly CancellationToken token;
  readonly CancellationTokenRegistration registration;
  readonly JavaScriptSerializer json=new JavaScriptSerializer {MaxJsonLength=MaxFrameBytes};
  int nextId;
  [DllImport("kernel32.dll",SetLastError=true)] static extern bool GetNamedPipeServerProcessId(SafePipeHandle pipe,out uint processId);
  internal DesktopAppClient(NamedPipeClientStream pipe,CancellationToken token) {
   this.pipe=pipe;this.token=token;registration=token.Register(()=>pipe.Dispose());
  }
  internal static IDesktopAppClient TryConnect(string executable,CancellationToken token) {
   string directory=Path.GetFullPath(Path.Combine(Path.GetDirectoryName(executable),".."));
   var candidates=new List<NamedPipeClientStream>();
   try {
    // Rediscover after app restarts; never trust a persisted or inherited pipe path.
    foreach(string path in Directory.GetFiles(@"\\.\pipe\","codex-app-tools-*")) {
     token.ThrowIfCancellationRequested();string name=Path.GetFileName(path);Guid id;
     if(!name.StartsWith("codex-app-tools-",StringComparison.Ordinal)||!Guid.TryParseExact(name.Substring(16),"D",out id))continue;
     var candidate=new NamedPipeClientStream(".",name,PipeDirection.InOut,PipeOptions.Asynchronous,TokenImpersonationLevel.Identification);
     try {
      candidate.Connect(200);uint processId;
      if(!GetNamedPipeServerProcessId(candidate.SafePipeHandle,out processId))continue;
      using(var process=Process.GetProcessById((int)processId)) {
       string owner=process.MainModule.FileName;
       // Only the installed desktop beside the located Codex backend may receive prompts.
       if(!string.Equals(Path.GetDirectoryName(owner),directory,StringComparison.OrdinalIgnoreCase)||
          !(string.Equals(Path.GetFileName(owner),"ChatGPT.exe",StringComparison.OrdinalIgnoreCase)||string.Equals(Path.GetFileName(owner),"Codex.exe",StringComparison.OrdinalIgnoreCase)))continue;
      }
      candidates.Add(candidate);candidate=null;
     }catch(TimeoutException){}catch(IOException){}catch(UnauthorizedAccessException){}catch(ArgumentException){}catch(System.ComponentModel.Win32Exception){}
     finally {if(candidate!=null)candidate.Dispose();}
    }
    token.ThrowIfCancellationRequested();if(candidates.Count==0)return null;
    if(candidates.Count!=1)throw new IOException("Ambiguous Codex desktop messaging pipes.");
    var selected=candidates[0];candidates.Clear();return new DesktopAppClient(selected,token);
   }finally {foreach(var candidate in candidates)candidate.Dispose();}
  }
  internal static byte[] Frame(string body) {
   var bytes=Encoding.UTF8.GetBytes(body);if(bytes.Length==0||bytes.Length>MaxFrameBytes)throw new IOException("Invalid desktop message size.");
   var framed=new byte[bytes.Length+4];Buffer.BlockCopy(BitConverter.GetBytes(bytes.Length),0,framed,0,4);Buffer.BlockCopy(bytes,0,framed,4,bytes.Length);return framed;
  }
  byte[] ReadExactly(int size) {
   var bytes=new byte[size];int offset=0;
   while(offset<size) {token.ThrowIfCancellationRequested();int count=pipe.Read(bytes,offset,size-offset);if(count==0)throw new EndOfStreamException();offset+=count;}
   return bytes;
  }
  public Dictionary<string,object> Request(string method,object parameters) {
   token.ThrowIfCancellationRequested();int id=++nextId;
   try {
    byte[] frame=Frame(json.Serialize(new {jsonrpc="2.0",id=id,method=method,@params=parameters}));pipe.Write(frame,0,frame.Length);pipe.Flush();
    int length=BitConverter.ToInt32(ReadExactly(4),0);if(length<=0||length>MaxFrameBytes)throw new IOException("Invalid desktop response size.");
    var response=json.Deserialize<Dictionary<string,object>>(new UTF8Encoding(false,true).GetString(ReadExactly(length)));
    if(AppServer.Text(response,"jsonrpc")!="2.0"||!object.Equals(AppServer.Get(response,"id"),id))throw new IOException("Invalid desktop response identity.");
    if(AppServer.Get(response,"error")!=null)throw new IOException("Codex desktop rejected the messaging request.");
    var result=AppServer.Get(response,"result") as Dictionary<string,object>;
    if(result==null)throw new IOException("Invalid desktop response.");return result;
   }catch {token.ThrowIfCancellationRequested();throw;}
  }
  public void Dispose(){registration.Dispose();pipe.Dispose();}
 }
 internal static class DesktopChatActions {
  internal const string SubmittedMessage="Message sent to the existing chat in Codex. Follow its progress in the app.";
  internal static SendResult TrySend(ResetActionSettings settings,Action<string> prepared,Action claim,Action<string,string> accepted,CancellationToken cancel) {
   using(var budget=CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
    budget.CancelAfter(30000);
    IDesktopAppClient client;
    try {
     client=DesktopAppClient.TryConnect(CodexClient.Locate(budget.Token),budget.Token);
    }catch(Exception error) {
     if(!cancel.IsCancellationRequested)DiagnosticLog.Record(DiagnosticOperation.ChatSend,error);
     return new SendResult {Outcome=SendOutcome.Attention,Message=cancel.IsCancellationRequested?"Stopped before sending.":"Needs attention: could not connect to Codex desktop messaging. No message sent. Check Codex, then arm again."};
    }
    if(client==null)return null;
    try {return Send(settings,prepared,claim,accepted,budget.Token,client);}
    finally {try{client.Dispose();}catch(Exception error){DiagnosticLog.Record(DiagnosticOperation.ChatSend,error);}}
   }
  }
  // Acknowledgement means accepted delivery, not model completion. The app owns
  // the turn, tools, approvals, and cancellation after this handoff.
  internal static SendResult Send(ResetActionSettings settings,Action<string> prepared,Action claim,Action<string,string> accepted,CancellationToken token,IDesktopAppClient client) {
   string target=settings.DedicatedChat?settings.DedicatedThreadId:settings.ThreadId;bool attempted=false;
   try {
    CheckCapabilities(client);
    var info=Call(client,"read_thread",target,settings.RunId,new {threadId=target,includeTurns=false});
    var thread=AppServer.Get(info,"thread") as Dictionary<string,object>;
    if(!object.Equals(AppServer.Get(info,"schemaVersion"),1)||AppServer.Text(thread,"id")!=target||AppServer.Text(thread,"kind")!="codex"||AppServer.Text(thread,"hostId")!="local")throw new IOException("Invalid desktop chat identity.");
    string status=AppServer.Text(AppServer.Get(thread,"status") as Dictionary<string,object>,"type");
    if(status=="active")return new SendResult {Outcome=SendOutcome.Busy,ThreadId=target,Message=ChatActions.ActiveChatMessage};
    if(status!="idle"&&status!="notLoaded"&&status!="systemError")throw new IOException("Unknown desktop chat status.");
    prepared(target);token.ThrowIfCancellationRequested();claim();attempted=true;
    var response=Call(client,"send_message_to_thread",target,settings.RunId,new {threadId=target,hostId="local",prompt=settings.DedicatedChat?ResetMessages.Acknowledgement:settings.Message});
    if(AppServer.Text(response,"threadId")!=target)throw new IOException("Invalid desktop delivery acknowledgement.");
    accepted(target,null);return new SendResult {Outcome=SendOutcome.Submitted,ThreadId=target,Message=SubmittedMessage};
   }catch(Exception error) {
    // A confirmed missing acknowledgement chat retains its existing replacement
    // workflow. Other failures never switch destination or try another transport.
    var rpc=error as RpcException;
    if(!attempted&&settings.DedicatedChat&&rpc!=null&&rpc.MissingThread)return null;
    if(!token.IsCancellationRequested)DiagnosticLog.Record(DiagnosticOperation.ChatSend,error);
    return new SendResult {Outcome=attempted?SendOutcome.Uncertain:SendOutcome.Attention,ThreadId=target,Message=attempted?"Delivery uncertain. Inspect the chat before explicitly arming again.":token.IsCancellationRequested?"Stopped before sending.":"Needs attention: Codex desktop messaging is unavailable or incompatible. No message sent. Check Codex, then arm again."};
   }
  }
  static void CheckCapabilities(IDesktopAppClient client) {
   var tools=AppServer.Get(client.Request("tools/list",new {threadStartKind="all"}),"tools") as IEnumerable;
   if(tools==null)throw new IOException("Missing desktop messaging tools.");bool read=false,send=false;
   foreach(var item in tools) {
    var tool=item as Dictionary<string,object>;if(AppServer.Text(tool,"namespace")!="codex_app")continue;
    string name=AppServer.Text(tool,"name");if(name!="read_thread"&&name!="send_message_to_thread")continue;
    var schema=AppServer.Get(tool,"inputSchema") as Dictionary<string,object>;
    var properties=AppServer.Get(schema,"properties") as Dictionary<string,object>;
    if(AppServer.Text(schema,"type")!="object"||properties==null||PropertyType(properties,"threadId")!="string"||PropertyType(properties,name=="read_thread"?"includeTurns":"prompt")!=(name=="read_thread"?"boolean":"string")||name=="send_message_to_thread"&&PropertyType(properties,"hostId")!="string")throw new IOException("Incompatible desktop messaging tools.");
    var required=AppServer.Get(schema,"required") as IEnumerable;if(required==null)throw new IOException("Missing desktop tool requirements.");
    foreach(var value in required)if(!new[]{"threadId","hostId",name=="read_thread"?"includeTurns":"prompt"}.Contains(value as string))throw new IOException("Unknown desktop tool requirement.");
    if(name=="read_thread")read=true;else send=true;
   }
   if(!read||!send)throw new IOException("Missing desktop messaging tools.");
  }
  static string PropertyType(Dictionary<string,object> properties,string name){return AppServer.Text(AppServer.Get(properties,name) as Dictionary<string,object>,"type");}
  internal static Dictionary<string,object> Call(IDesktopAppClient client,string tool,string target,string runId,object arguments) {
   // Explicit widget request IDs; no borrowed live turn, call, token, or credentials.
   var response=client.Request("tools/call",new {callerSource="codex",@namespace="codex_app",threadId=target,turnId="widget-reset-"+runId,callId="widget-"+Guid.NewGuid().ToString("N"),tool=tool,arguments=arguments});
   if(!object.Equals(AppServer.Get(response,"success"),true)) {
    var failed=AppServer.Get(response,"contentItems") as IEnumerable;
    string reason="Codex desktop did not accept the request.";
    if(failed!=null)foreach(var item in failed) {
     var block=item as Dictionary<string,object>;string message=AppServer.Text(block,"text");
     if(AppServer.Text(block,"type")=="inputText"&&!string.IsNullOrEmpty(message)){reason=message;break;}
    }
    throw new RpcException(reason);
   }
   var content=AppServer.Get(response,"contentItems") as IEnumerable;if(content==null)throw new IOException("Missing desktop result.");
   Dictionary<string,object> result=null;
   foreach(var item in content) {
    var block=item as Dictionary<string,object>;if(result!=null||AppServer.Text(block,"type")!="inputText")throw new IOException("Invalid desktop result.");
    result=new JavaScriptSerializer {MaxJsonLength=DesktopAppClient.MaxFrameBytes}.Deserialize<Dictionary<string,object>>(AppServer.Text(block,"text"));
   }
   if(result==null)throw new IOException("Missing desktop result.");return result;
  }
 }
}
