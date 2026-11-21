using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace CodexUsageBubble {
 internal sealed class ResetActionSettings {
  public int Version=1;
  public bool Armed,Repeat,NewChat,DedicatedChat;
  public string DedicatedThreadId;
  public string ThreadId,ThreadTitle,ProjectPath,Message="Continue",RunId;
  public long DueResetUnix,LastHandledResetUnix;
		public long ManualSendUnix;
  public string Status="Not armed.",LastThreadId,LastTurnId;
  internal ResetActionSettings Copy(){return (ResetActionSettings)MemberwiseClone();}
 }
 internal sealed class ResetActionStore {
  readonly string path;
  internal ResetActionStore(string path){this.path=path;}
  internal ResetActionSettings Load() {
   try {
    if(!File.Exists(path))return new ResetActionSettings();
    var state=new JavaScriptSerializer().Deserialize<ResetActionSettings>(File.ReadAllText(path));
    if(state==null||state.Version!=1||state.DueResetUnix<0||state.DueResetUnix>ResetActionCoordinator.MaxResetUnix||state.LastHandledResetUnix<0||state.LastHandledResetUnix>253402300799L||state.ManualSendUnix<0||state.ManualSendUnix>253402300799L)throw new IOException();
    if(state.Armed)Validate(state);
    if(state.Armed&&Canceled(state.RunId)){
     state.Armed=false;state.ManualSendUnix=0;state.Status="Canceled. Any previously submitted message may still be running.";
    }
    if(state.Armed&&Claimed(state.RunId)){state.Armed=false;state.Status="Delivery uncertain. Inspect the chat before explicitly arming again.";}
    return state;
   }catch(Exception error){DiagnosticLog.Record(DiagnosticOperation.ActionSettingsLoad,error);return new ResetActionSettings {Status="Needs attention: saved reset action could not be read. Arm it again explicitly."};}
  }
  internal static void Validate(ResetActionSettings value) {
   if(string.IsNullOrWhiteSpace(value.Message)||value.Message.Length>20000)throw new ArgumentException("Enter a message of 1–20,000 characters.");
   if(value.NewChat&&value.DedicatedChat)throw new ArgumentException("Choose one chat destination.");
   if(value.NewChat||value.DedicatedChat) {if(string.IsNullOrWhiteSpace(value.ProjectPath)||!Directory.Exists(value.ProjectPath))throw new ArgumentException("Choose an existing project folder for the new chat.");}
   else if(string.IsNullOrWhiteSpace(value.ThreadId))throw new ArgumentException("Select an existing chat from the list.");
  }
  string DispatchPath(string runId,string extension) {
   Guid id;if(!Guid.TryParseExact(runId,"N",out id))throw new IOException("Invalid dispatch identity.");
   return Path.Combine(Path.GetDirectoryName(path),"reset-action-receipts",runId+extension);
  }
  internal bool Canceled(string runId) {
   try {File.GetAttributes(DispatchPath(runId,".cancel"));return true;}
   catch(FileNotFoundException){return false;}
   catch(DirectoryNotFoundException){return false;}
  }
  internal void RecordCancellation(string runId) {
   AtomicFile.Write(DispatchPath(runId,".cancel"),"This action was canceled. Do not submit it.");
  }
  internal bool Claimed(string runId){return File.Exists(DispatchPath(runId,".claim"));}
  internal void Claim(string runId) {
   if(Canceled(runId))throw new OperationCanceledException();
   string file=DispatchPath(runId,".claim");Directory.CreateDirectory(Path.GetDirectoryName(file));
   using(var stream=new FileStream(file,FileMode.CreateNew,FileAccess.Write,FileShare.Read,4096,FileOptions.WriteThrough)) {
    var bytes=System.Text.Encoding.UTF8.GetBytes("Submission may have started. Do not resend automatically.");stream.Write(bytes,0,bytes.Length);stream.Flush(true);
   }
  }
  internal void Receipt(ResetActionSettings state){AtomicFile.Write(DispatchPath(state.RunId,".json"),new JavaScriptSerializer().Serialize(state));}
  internal void Save(ResetActionSettings state){AtomicFile.Write(path,new JavaScriptSerializer().Serialize(state));}
  internal string DedicatedWorkspace {get{return Path.Combine(Path.GetDirectoryName(path),"acknowledgement-chat");}}
 }
 // Shared by action state and preferences: never truncate the previous settings file.
 internal static class AtomicFile {
  [DllImport("kernel32.dll",CharSet=CharSet.Unicode,SetLastError=true)] static extern bool MoveFileEx(string source,string destination,int flags);
  internal static void Write(string destination,string content) {
   string temporary=null;
   try {
    string directory=Path.GetDirectoryName(destination);
    Directory.CreateDirectory(directory);
    temporary=Path.Combine(directory,Guid.NewGuid().ToString("N")+".tmp");
    using(var stream=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,4096,FileOptions.WriteThrough)) {
     var bytes=new System.Text.UTF8Encoding(false).GetBytes(content);stream.Write(bytes,0,bytes.Length);stream.Flush(true);
    }
    // Atomic same-directory replacement with write-through. Brief sharing conflicts can
    // occur while Windows indexes the last receipt; never fall back to a partial write.
    for(int attempt=0;;attempt++) {
     if(MoveFileEx(temporary,destination,0x1|0x8))break;
     int code=Marshal.GetLastWin32Error();
     if(attempt>=5)throw new IOException("Could not atomically save settings.",new System.ComponentModel.Win32Exception(code));
     Thread.Sleep(20*(attempt+1));
    }
   }catch(Exception error){DiagnosticLog.Record(DiagnosticOperation.SettingsWrite,error);throw;}
   finally{if(File.Exists(temporary))File.Delete(temporary);}
  }
 }
 // Owns the state machine, not a timer. Only successful FRESH account reads can dispatch.
 internal sealed class ResetActionCoordinator : IDisposable {
  internal const int SendDelaySeconds=60;
  internal const long MaxResetUnix=253402300799L-SendDelaySeconds;
  readonly object gate=new object();
  readonly ResetActionStore store;
  readonly Func<DateTimeOffset> now;
  readonly Func<ResetActionSettings,Action<string>,Action,Action<string,string>,CancellationToken,SendResult> send;
  ResetActionSettings state;
  CancellationTokenSource active;
  DateTimeOffset retryAt;
  const string CancellationSaveError="Needs attention: cancellation could not be saved. Retry Cancel pending action before closing; the saved action may resume after restart.";
  bool running,disposed,dispatchPaused,deferred,cancellationSaveFailed;
  internal event Action Changed;
  internal ResetActionCoordinator(string path,Func<DateTimeOffset> clock,Func<ResetActionSettings,Action<string>,Action,Action<string,string>,CancellationToken,SendResult> sender) {
   store=new ResetActionStore(path);now=clock;send=sender??ChatActions.Send;state=store.Load();
  }
  internal ResetActionSettings State {get{lock(gate)return state.Copy();}}
  internal bool Running {get{lock(gate)return running;}}
  internal bool CanCancel {get{lock(gate)return state.Armed||cancellationSaveFailed;}}
		internal DateTimeOffset CurrentTime { get { return now(); } }
  internal DateTimeOffset? NextCheck {
   get {lock(gate) {
    if(disposed||!state.Armed||running)return null;
    var due=state.ManualSendUnix>0||state.DueResetUnix>0?SendAt(state):now();
    return retryAt>due?retryAt:due;
   }}
  }
  internal static long Unix(DateTimeOffset value){return (long)(value-new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero)).TotalSeconds;}
  internal static DateTimeOffset FromUnix(long value){return new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(value);}
  internal static DateTimeOffset SendAt(long reset){return FromUnix(reset).AddSeconds(SendDelaySeconds);}
  static long FutureReset(UsageSnapshot value,DateTimeOffset time) {
   if(value==null||value.FiveHour==null||!value.FiveHour.HasReset)return 0;
   long reset=Unix(value.FiveHour.Reset);
   return reset>Unix(time)&&reset<=MaxResetUnix?reset:0;
  }
		internal static DateTimeOffset SendAt(ResetActionSettings settings) {
			return settings.ManualSendUnix > 0 ? FromUnix(settings.ManualSendUnix) : SendAt(settings.DueResetUnix);
		}

		internal void SetManualSendTime(DateTimeOffset? time) {
			lock (gate) {
				if (disposed || running || !state.Armed) { throw new InvalidOperationException("Arm an action and wait for any current message to finish before changing its send time."); }

				long timestamp = time.HasValue ? Unix(time.Value) : 0;
				ValidateManualTime(timestamp);
				ResetActionSettings next = state.Copy();
				next.ManualSendUnix = timestamp;
				next.Status = timestamp > 0 ? "Armed for the selected send time." : "Armed for the next five-hour reset.";
				store.Save(next);
				state = next;
				retryAt = now();
			}

			Notify();
		}

		void ValidateManualTime(long timestamp) {
			if (timestamp < 0 || timestamp > 253402300799L || (timestamp > 0 && FromUnix(timestamp) <= now())) {
				throw new ArgumentException("Choose a send time in the future.");
			}
		}

  internal void Arm(ResetActionSettings settings,UsageSnapshot latest) {
   lock(gate) {
    if(running||disposed)throw new InvalidOperationException("Wait for the current message to finish before arming another action.");
    var next=settings.Copy();next.Version=1;next.Armed=true;next.RunId=Guid.NewGuid().ToString("N");next.DueResetUnix=0;next.LastHandledResetUnix=0;next.LastThreadId=null;next.LastTurnId=null;
    // Retain the acknowledgement chat even while another destination is selected.
    next.DedicatedThreadId=state.DedicatedThreadId;
    if(next.DedicatedChat) {
     next.NewChat=false;next.ProjectPath=store.DedicatedWorkspace;next.Message=ResetMessages.Acknowledgement;
     Directory.CreateDirectory(next.ProjectPath);
    }
    ResetActionStore.Validate(next);
				ValidateManualTime(next.ManualSendUnix);
    next.Status="Waiting for a server-reported future reset.";
    next.DueResetUnix=FutureReset(latest,now());
    if(next.DueResetUnix>0)next.Status="Armed for the next five-hour reset.";
				if (next.ManualSendUnix > 0) { next.Status = "Armed for the selected send time."; }
    store.Save(next);state=next;cancellationSaveFailed=false;retryAt=now();
   }
   Notify();
  }
  internal void CancelPending() {
   lock(gate) {
    if(!state.Armed&&!cancellationSaveFailed)return;

    state.Armed=false;
    bool claimed=store.Claimed(state.RunId);
    if(running&&!claimed)active.Cancel();
    state.ManualSendUnix=0;
    if(!running)state.Status=claimed?"Canceled. Any previously submitted message may still be running.":"Canceled. No message sent.";

    // Either write is sufficient. An open settings file can block replacement
    // while the separate cancellation record can still be written.
    bool recorded=false;
    try {store.RecordCancellation(state.RunId);recorded=true;}
    catch(Exception error){DiagnosticLog.Record(DiagnosticOperation.ActionCancellation,error);}
    try {store.Save(state);cancellationSaveFailed=false;}
    catch(Exception) {
     cancellationSaveFailed=!recorded;
     if(cancellationSaveFailed)state.Status=CancellationSaveError;
    }
   }
   Notify();
  }
  internal void Stop() {
   lock(gate){state.Armed=false;if(active!=null)active.Cancel();else state.Status="Stopped.";Persist();}
   Notify();
  }
  internal void SetPaused(bool paused) {
   lock(gate) {
    dispatchPaused=paused;
    if(paused&&active!=null&&!store.Claimed(state.RunId)){deferred=true;active.Cancel();}
    if(!paused)retryAt=now();
   }
   Notify();
  }
  internal void ReadFailed(){lock(gate){if(state.Armed&&!running)retryAt=now().AddSeconds(60);}Notify();}
  internal void Observe(UsageSnapshot value,bool paused,DateTimeOffset requestStarted) {
   ResetActionSettings dispatch=null;
   lock(gate) {
    if(disposed||!state.Armed||running||paused||dispatchPaused)return;
    var time=now();
    if(state.ManualSendUnix==0&&state.DueResetUnix==0) {
     long reset=FutureReset(value,time);
     if(reset>0&&reset>state.LastHandledResetUnix) {state.DueResetUnix=reset;state.Status="Armed for the next five-hour reset.";if(!Persist())return;}
     else {retryAt=time.AddSeconds(30);return;}
    }
    // Bind a pending cycle: a post-sleep reset moving ahead must not erase its missed action.
    if(time<SendAt(state)||time<retryAt||requestStarted<SendAt(state))return;
    if(value.FiveHour.Remaining<=0||value.Weekly.Remaining<=0||(state.ManualSendUnix==0&&value.FiveHour.HasReset&&Unix(value.FiveHour.Reset)<=state.DueResetUnix&&value.FiveHour.Remaining<100)||(state.ManualSendUnix>0&&value.FiveHour.IsExpired(time)&&value.FiveHour.Remaining<100)||(value.Weekly.IsExpired(time)&&value.Weekly.Remaining<100)) {state.Status="Waiting for available five-hour and weekly allowance.";retryAt=time.AddSeconds(60);Persist();return;}
    state.Status="Preparing background chat…";
    if(!Persist())return;
    running=true;deferred=false;active=new CancellationTokenSource();dispatch=state.Copy();
   }
   Notify();RunAsync(dispatch);
  }
  async void RunAsync(ResetActionSettings dispatch) {
   SendResult result;
   try {
    var token=active.Token;
    result=await Task.Run(()=>send(dispatch,
     thread=>{lock(gate){
      if(dispatch.DedicatedChat){state.DedicatedThreadId=thread;state.LastThreadId=thread;store.Save(state);}
      token.ThrowIfCancellationRequested();state.LastThreadId=thread;store.Save(state);store.Receipt(state);
     }},
     ()=>{lock(gate){token.ThrowIfCancellationRequested();if(dispatchPaused)throw new OperationCanceledException();store.Claim(dispatch.RunId);state.Status="Sending message…";store.Save(state);}},
     (thread,turn)=>{lock(gate){state.LastThreadId=thread;state.LastTurnId=turn;state.Status=turn==null?"Message accepted by Codex…":"Running in the background…";store.Save(state);store.Receipt(state);}Notify();},token));
   }catch(Exception error){DiagnosticLog.Record(DiagnosticOperation.ActionDispatch,error);result=new SendResult {Outcome=SendOutcome.Uncertain,Message="Delivery uncertain. Inspect the chat before explicitly arming again."};}
   lock(gate) {
    running=false;active.Dispose();active=null;
    state.Status=result.Message;
    if(result.ThreadId!=null)state.LastThreadId=result.ThreadId;
    if(result.TurnId!=null)state.LastTurnId=result.TurnId;
    if((deferred||disposed)&&state.Armed&&!store.Claimed(dispatch.RunId)) {state.Status="Waiting to resume the pending reset action.";retryAt=now();}
    else if(result.Outcome==SendOutcome.Busy&&state.Armed&&!disposed)retryAt=now().AddSeconds(60);
    else if(result.Outcome==SendOutcome.Completed||result.Outcome==SendOutcome.Submitted) {
     // Record completion before preparing a new cycle; a crash can only suppress, never duplicate.
     bool repeat=state.Repeat&&state.Armed&&!disposed;
     state.Armed=false;
     try {store.Receipt(state);store.Save(state);}catch(Exception error){DiagnosticLog.Record(DiagnosticOperation.ActionCompletion,error);repeat=false;state.Status="Needs attention: could not save completion. Inspect the chat before rearming.";}
					// A manual send consumes its override. Recurrence follows the next server reset,
					// retaining a still-future reset instead of inventing a new five-hour period.
					if (state.ManualSendUnix == 0 || state.DueResetUnix <= Unix(now())) {
						state.LastHandledResetUnix = Math.Max(state.LastHandledResetUnix, state.DueResetUnix);
					}

					state.ManualSendUnix = 0;
					state.DueResetUnix = 0;
     state.Armed=repeat;state.RunId=Guid.NewGuid().ToString("N");retryAt=now();
    }else {
     state.Armed=false;
     if(store.Claimed(dispatch.RunId)) {
      try{store.Receipt(state);}catch(Exception error){DiagnosticLog.Record(DiagnosticOperation.ActionReceipt,error);state.Status="Needs attention: could not save the final receipt. Inspect the chat before rearming.";}
     }
    }
    Persist();
   }
   Notify();
  }
  bool Persist() {
   try{store.Save(state);cancellationSaveFailed=false;return true;}
   catch(Exception){
    state.Armed=false;
    state.Status=cancellationSaveFailed?CancellationSaveError:"Needs attention: reset-action settings could not be saved. Check the saved action before restarting.";
    return false;
   }
  }
  void Notify(){var changed=Changed;if(changed!=null)changed();}
  public void Dispose(){lock(gate){disposed=true;if(active!=null)active.Cancel();}}
 }
}
