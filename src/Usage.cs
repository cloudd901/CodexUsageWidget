using System;
namespace CodexUsageBubble {
 public sealed class UsageException : Exception { public UsageException(string message) : base(message) {} }
 public sealed class UsageWindow {
  public double Remaining;
  // MinValue means the server did not supply a reset timestamp.
  public DateTimeOffset Reset;
  public bool HasReset {get{return Reset!=DateTimeOffset.MinValue;}}
  public bool IsExpired(DateTimeOffset now){return HasReset&&Reset<=now;}
 }
 public sealed class UsageSnapshot {
  public UsageWindow FiveHour, Weekly;
  public static UsageSnapshot Parse(string json) {
   try {
    var root = new System.Web.Script.Serialization.JavaScriptSerializer().Deserialize<System.Collections.Generic.Dictionary<string,object>>(json);
    if(root.ContainsKey("error")) throw new UsageException("Codex rejected the usage read. Check that Codex is signed in with a ChatGPT account.");
    var result = root["result"] as System.Collections.Generic.Dictionary<string,object>;
    System.Collections.Generic.Dictionary<string,object> limits = null;
    object mapObj;
    if(result.TryGetValue("rateLimitsByLimitId",out mapObj) && mapObj is System.Collections.Generic.Dictionary<string,object>) {
     var map=(System.Collections.Generic.Dictionary<string,object>)mapObj;object codex;
     if(map.TryGetValue("codex",out codex)) limits=codex as System.Collections.Generic.Dictionary<string,object>;
    }
    if(limits==null) limits=result["rateLimits"] as System.Collections.Generic.Dictionary<string,object>;
    if(limits==null) throw new UsageException("Codex returned no account rate limits. Check the Codex account sign-in.");
    var s=new UsageSnapshot();
    foreach(var key in new[]{"primary","secondary"}) {
     object obj;if(!limits.TryGetValue(key,out obj)||obj==null)continue;
     var w=obj as System.Collections.Generic.Dictionary<string,object>;
     var duration=Convert.ToInt32(w["windowDurationMins"]);
     if(duration!=300&&duration!=10080)continue;
     if(!w.ContainsKey("usedPercent")||w["usedPercent"]==null)throw new UsageException("Codex returned an incomplete usage window.");
     var used=Convert.ToDouble(w["usedPercent"]);
     object resetValue;bool hasReset=w.TryGetValue("resetsAt",out resetValue)&&resetValue!=null;
     long unix=hasReset?Convert.ToInt64(resetValue):0;
     if(double.IsNaN(used)||double.IsInfinity(used)||used<0||used>100||(hasReset&&unix<=0))throw new UsageException("Codex returned invalid usage values.");
     var window=new UsageWindow {Remaining=100-used, Reset=hasReset?new DateTimeOffset(1970,1,1,0,0,0,TimeSpan.Zero).AddSeconds(unix):DateTimeOffset.MinValue};
     if(duration==300)s.FiveHour=window;else s.Weekly=window;
    }
    if(s.FiveHour==null||s.Weekly==null)throw new UsageException("Codex did not return both the 5-hour and weekly usage windows.");
    return s;
   }catch(UsageException){throw;}catch(Exception){throw new UsageException("Codex returned an unsupported or incomplete rate-limit response.");}
  }
 }
}
