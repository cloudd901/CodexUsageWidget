using System;
namespace CodexUsageBubble {
 public sealed class AdaptivePolling {
  public TimeSpan CurrentInterval {get;private set;}
  public TimeSpan? NextPollAt {get;private set;}
  public TimeSpan? LastChangeAt {get;private set;}
  public bool Paused {get;private set;}
  public int ConsecutiveFailures {get;private set;}
  public AdaptivePolling() {CurrentInterval=TimeSpan.FromSeconds(30);NextPollAt=TimeSpan.Zero;}
  public void Failed(TimeSpan now) {
   ConsecutiveFailures=Math.Min(ConsecutiveFailures+1,1000);
   CurrentInterval=TimeSpan.FromSeconds(Math.Min(900,Math.Max(30,CurrentInterval.TotalSeconds)*2));
   NextPollAt=Paused?(TimeSpan?)null:now+CurrentInterval;
  }
  public void Pause() {Paused=true;NextPollAt=null;}
  public void Resume(TimeSpan now) {Paused=false;NextPollAt=now;}
  public bool IsDue(TimeSpan now) {return NextPollAt.HasValue&&now>=NextPollAt.Value;}
  double lastFive,lastWeek;
  DateTimeOffset lastFiveReset,lastWeekReset;
  public void Succeeded(UsageSnapshot value,TimeSpan now) {
   if(value==null||value.FiveHour==null||value.Weekly==null)throw new ArgumentException("A complete successful usage response is required.");
   if(!LastChangeAt.HasValue||value.FiveHour.Remaining!=lastFive||value.Weekly.Remaining!=lastWeek||value.FiveHour.Reset!=lastFiveReset||value.Weekly.Reset!=lastWeekReset)LastChangeAt=now;
   lastFive=value.FiveHour.Remaining;lastWeek=value.Weekly.Remaining;lastFiveReset=value.FiveHour.Reset;lastWeekReset=value.Weekly.Reset;
   double minutes=(now-LastChangeAt.Value).TotalMinutes;
   CurrentInterval=TimeSpan.FromSeconds(minutes>=120?300:minutes>=60?180:minutes>=30?120:minutes>=5?60:30);
   ConsecutiveFailures=0;
   NextPollAt=Paused?(TimeSpan?)null:now+CurrentInterval;
  }
 }
}
