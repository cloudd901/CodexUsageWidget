using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;

namespace CodexUsageBubble {
 internal sealed class ChatEntry {
  public string Id,Title,Cwd;
  public long Updated;
  public string DisplayTitle {get{return Compact(Title,96);}}
  public string FolderDisplay {get{return Cwd;}}
  public string ActivityDisplay {get{return new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(Updated).ToLocalTime().ToString("g")+" · "+Id;}}
  internal static string Compact(string value,int limit) {
   if(string.IsNullOrWhiteSpace(value))return "Untitled chat";
   var text=new StringBuilder();bool space=false;
   foreach(char c in value) {
    if(char.IsWhiteSpace(c)){space=text.Length>0;continue;}
    if(space){text.Append(' ');space=false;}text.Append(c);
    if(text.Length>limit) {
     int length=limit;if(char.IsHighSurrogate(text[length-1]))length--;
     return text.ToString(0,length).TrimEnd()+"…";
    }
   }
   return text.ToString();
  }
  public override string ToString(){return DisplayTitle+"\n"+Compact(Cwd,120)+"\n"+ActivityDisplay;}
 }
 internal static class ResetMessages {
  internal const string Continue="Continue";
  internal const string Acknowledgement="Reply only with OK. Do not use tools.";
  internal const string ReviewOrContinue="Review this chat and its current work state. If any previously requested or approved work remains unfinished, continue it within the established scope and constraints, and perform only the relevant remaining checks. If everything is already complete, briefly acknowledge completion and take no further action. Do not invent new tasks or repeat completed work.";
 }
 internal sealed class ChatPage {
  internal readonly List<ChatEntry> Chats=new List<ChatEntry>();
  internal string Cursor;
 }
 internal enum SendOutcome { Completed, Busy, Attention, Uncertain, Submitted }
 internal sealed class SendResult {
  internal SendOutcome Outcome;
  internal string ThreadId,TurnId,Message;
 }
 internal static class ChatActions {
  internal const string OwnershipBlockedMessage="Blocked: another client owns this chat and Codex desktop messaging is unavailable. No message sent; retrying every 60s. Check Codex is running and up to date.";
  internal const string ActiveChatMessage="Waiting for the current chat turn to finish. No message sent; retrying every 60s.";
  internal static ChatPage List(string cursor,CancellationToken cancel) {
   using(var budget=CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
    budget.CancelAfter(30000);
    using(var server=new AppServer(CodexClient.Locate(budget.Token),budget.Token)) {
     return ParsePage(server.Request("thread/list",new {cursor=cursor,limit=100,sortKey="updated_at",archived=false,sourceKinds=new[]{"cli","vscode","exec","appServer"},useStateDbOnly=true}));
    }
   }
  }
  internal static ChatPage ParsePage(Dictionary<string,object> result) {
   var page=new ChatPage {Cursor=AppServer.Text(result,"nextCursor")};
   var data=AppServer.Get(result,"data") as IEnumerable;
   if(data==null)throw new IOException("Codex did not return a chat list.");
   foreach(var value in data) {
    var item=value as Dictionary<string,object>;
    var id=AppServer.Text(item,"id");var cwd=AppServer.Text(item,"cwd");
    if(string.IsNullOrWhiteSpace(id)||string.IsNullOrWhiteSpace(cwd)||object.Equals(AppServer.Get(item,"canAcceptDirectInput"),false))continue;
    long updated;long.TryParse(Convert.ToString(AppServer.Get(item,"updatedAt")),out updated);
    var title=AppServer.Text(item,"name");if(string.IsNullOrWhiteSpace(title))title=AppServer.Text(item,"preview");
    page.Chats.Add(new ChatEntry {Id=id,Cwd=cwd,Title=string.IsNullOrWhiteSpace(title)?"Untitled chat":title,Updated=Math.Max(0,Math.Min(253402300799L,updated))});
   }
   return page;
  }
  // Callbacks are synchronous: the durable claim must finish BEFORE writing turn/start.
  internal static SendResult Send(ResetActionSettings settings,Action<string> prepared,Action claim,Action<string,string> accepted,CancellationToken cancel) {
   if(!settings.NewChat&&(!settings.DedicatedChat||!string.IsNullOrEmpty(settings.DedicatedThreadId))) {
    var desktop=DesktopChatActions.TrySend(settings,prepared,claim,accepted,cancel);if(desktop!=null)return desktop;
   }
   return SendBackground(settings,prepared,claim,accepted,cancel);
  }
  internal static SendResult SendBackground(ResetActionSettings settings,Action<string> prepared,Action claim,Action<string,string> accepted,CancellationToken cancel) {
   string thread=null,turn=null;bool attempted=false;
   using(var budget=CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
    budget.CancelAfter(30000);
    try {
     using(var server=new AppServer(CodexClient.Locate(budget.Token),budget.Token)) {
      try {
       Dictionary<string,object> response;
       bool create=settings.NewChat||(settings.DedicatedChat&&string.IsNullOrEmpty(settings.DedicatedThreadId));
       string target=settings.DedicatedChat?settings.DedicatedThreadId:settings.ThreadId;
       response=null;
       if(!create) {
        try {response=server.Request("thread/resume",new {threadId=target,excludeTurns=true});}
        catch(RpcException error) {
         // Only a confirmed missing acknowledgement chat may be replaced. Busy,
         // unavailable, and misconfigured chats retain their exact destination.
         if(!settings.DedicatedChat||!error.MissingThread)throw;
         create=true;
        }
       }
       if(create) {
        if(!Directory.Exists(settings.ProjectPath))throw new IOException("The selected project folder no longer exists.");
        response=server.Request("thread/start",new {cwd=settings.ProjectPath});
       }
       var info=AppServer.Get(response,"thread") as Dictionary<string,object>;
       thread=AppServer.Text(info,"id");
       if(string.IsNullOrEmpty(thread)||(!create&&thread!=target))throw new IOException("Codex returned a different chat identity.");
       var status=AppServer.Get(info,"status") as Dictionary<string,object>;
       if(AppServer.Text(status,"type")=="active")return new SendResult {Outcome=SendOutcome.Busy,ThreadId=thread,Message=ActiveChatMessage};
       prepared(thread);cancel.ThrowIfCancellationRequested();
       claim();attempted=true;
       response=server.Request("turn/start",new {threadId=thread,input=new[]{new {type="text",text=settings.DedicatedChat?ResetMessages.Acknowledgement:settings.Message}}});
       turn=AppServer.Text(AppServer.Get(response,"turn") as Dictionary<string,object>,"id");
       if(string.IsNullOrEmpty(turn))throw new IOException("Codex did not acknowledge the message.");
       accepted(thread,turn);budget.CancelAfter(Timeout.Infinite);
       while(true) {
        var notification=server.NextEvent();var parameters=AppServer.Get(notification,"params") as Dictionary<string,object>;
        if(AppServer.Text(parameters,"threadId")!=thread)continue;
        if(AppServer.Text(notification,"method")=="turn/completed") {
         var completed=AppServer.Get(parameters,"turn") as Dictionary<string,object>;
         if(AppServer.Text(completed,"id")!=turn)continue;
         bool ok=AppServer.Text(completed,"status")=="completed";
         return new SendResult {Outcome=ok?SendOutcome.Completed:SendOutcome.Attention,ThreadId=thread,TurnId=turn,Message=ok?"Completed. Message sent; check fresh usage for the next reset.":"Needs attention: the background turn did not complete. Open this chat in Codex."};
        }
       }
      }catch {if(thread!=null&&turn!=null)server.Interrupt(thread,turn);throw;}
     }
    }catch(RpcException error) {
     if(attempted||!error.Busy)DiagnosticLog.Record(DiagnosticOperation.ChatSend,error);
     return new SendResult {Outcome=!attempted&&error.Busy?SendOutcome.Busy:SendOutcome.Attention,ThreadId=thread,TurnId=turn,Message=!attempted&&error.Busy?(error.OwnedByAnotherClient?OwnershipBlockedMessage:ActiveChatMessage):"Needs attention: Codex rejected the background action. Open the selected chat and check its configuration."};
    }catch(AttentionException error) {
     DiagnosticLog.Record(DiagnosticOperation.ChatSend,error);
     return new SendResult {Outcome=SendOutcome.Attention,ThreadId=thread,TurnId=turn,Message=error.Message};
    }catch(Exception error) {
     if(!cancel.IsCancellationRequested)DiagnosticLog.Record(DiagnosticOperation.ChatSend,error);
     return new SendResult {Outcome=attempted?SendOutcome.Uncertain:SendOutcome.Attention,ThreadId=thread,TurnId=turn,Message=attempted?"Delivery uncertain. Inspect the chat before explicitly arming again.":cancel.IsCancellationRequested?"Stopped before sending.":"Needs attention: Codex could not prepare this chat. Check sign-in, project folder, and network, then arm again."};
    }
   }
  }
 }
}
