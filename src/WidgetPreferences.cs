using System;
using System.IO;
using System.Web.Script.Serialization;
using System.Collections.Generic;
using System.Globalization;

namespace CodexUsageBubble {
 internal sealed class WidgetPreferences {
  public int Version=1;
  public double? Left,Top;
  public double NormalWidth=272;
  public bool MinimalMode,Countdown,ResetTimeBackground,Snapping;
  public bool ShowLabelPrefixes=true,CloseToTray=true;
  public string Theme="Midnight",MinimalShape="Circle";
  public int MinimalSize=60;

  internal static bool Finite(double value){return !double.IsNaN(value)&&!double.IsInfinity(value);}
  internal void Normalize() {
   if(!Left.HasValue||!Top.HasValue||!Finite(Left.Value)||!Finite(Top.Value))Left=Top=null;
   NormalWidth=Finite(NormalWidth)?Math.Max(232,Math.Min(380,NormalWidth)):272;
   if(Theme!="Midnight"&&Theme!="Ocean"&&Theme!="Light")Theme="Midnight";
   if(MinimalShape!="Circle"&&MinimalShape!="Square")MinimalShape="Circle";
   if(MinimalSize==43)MinimalSize=40;else if(MinimalSize==62)MinimalSize=60;else if(MinimalSize==78)MinimalSize=80;else if(MinimalSize==96)MinimalSize=100;
   if(MinimalSize!=40&&MinimalSize!=60&&MinimalSize!=80&&MinimalSize!=100)MinimalSize=60;
  }
  internal static WidgetPreferences FromLegacy(double[] values) {
   if(values==null)throw new ArgumentException("Invalid legacy preferences.");
   var result=new WidgetPreferences();
   if(values.Length>=2){result.Left=values[0];result.Top=values[1];}
   if(values.Length>=3)result.NormalWidth=values[2];
   result.MinimalMode=values.Length>=4&&values[3]==1;
   result.Countdown=values.Length>=5&&values[4]==1;
   result.ShowLabelPrefixes=values.Length<6||values[5]==1;
   if(values.Length>=7&&values[6]>=0&&values[6]<3&&values[6]==Math.Floor(values[6]))result.Theme=new[]{"Midnight","Ocean","Light"}[(int)values[6]];
   if(values.Length>=8&&Finite(values[7])&&values[7]>=int.MinValue&&values[7]<=int.MaxValue&&values[7]==Math.Floor(values[7]))result.MinimalSize=(int)values[7];
   result.ResetTimeBackground=values.Length>=9&&values[8]==1;
   result.Snapping=values.Length>=10&&values[9]==1;
   result.MinimalShape=values.Length>=11&&values[10]==1?"Square":"Circle";
   // Slot 12 belonged to a retired icon preference; never migrate it.
   result.CloseToTray=values.Length<13||values[12]!=0;
   result.Normalize();return result;
  }
  static T Named<T>(Dictionary<string,object> values,string key,T fallback) {
   object value;return values.TryGetValue(key,out value)&&value is T?(T)value:fallback;
  }
  static double? Number(Dictionary<string,object> values,string key) {
   object value;
   if(!values.TryGetValue(key,out value)||!(value is int||value is long||value is double||value is decimal))return null;
   return Convert.ToDouble(value,CultureInfo.InvariantCulture);
  }
  internal static WidgetPreferences FromNamed(Dictionary<string,object> values) {
   var result=new WidgetPreferences {
    Left=Number(values,"Left"),Top=Number(values,"Top"),NormalWidth=Number(values,"NormalWidth")??272,
    MinimalMode=Named(values,"MinimalMode",false),Countdown=Named(values,"Countdown",false),
    ResetTimeBackground=Named(values,"ResetTimeBackground",false),Snapping=Named(values,"Snapping",false),
    ShowLabelPrefixes=Named(values,"ShowLabelPrefixes",true),CloseToTray=Named(values,"CloseToTray",true),
    Theme=Named(values,"Theme","Midnight"),MinimalShape=Named(values,"MinimalShape","Circle"),MinimalSize=Named(values,"MinimalSize",60)
   };
   result.Normalize();return result;
  }
 }
 internal sealed class WidgetPreferencesStore {
  readonly string path;
  bool legacy,loaded;
  internal bool CanSave {get;private set;}
  internal string LoadedText {get;private set;}
  internal WidgetPreferencesStore(string path){this.path=path;CanSave=true;}
  internal WidgetPreferences Load() {
   loaded=true;CanSave=true;legacy=false;LoadedText=null;
   try {
    if(!File.Exists(path))return new WidgetPreferences();
    LoadedText=File.ReadAllText(path);
    var json=new JavaScriptSerializer();
    if(LoadedText.TrimStart().StartsWith("[",StringComparison.Ordinal)) {
     var preferences=WidgetPreferences.FromLegacy(json.Deserialize<double[]>(LoadedText));legacy=true;return preferences;
    }
    var root=json.DeserializeObject(LoadedText) as Dictionary<string,object>;
    object version;
    if(root==null||!root.TryGetValue("Version",out version)||!(version is int)||!object.Equals(version,1)) {
     CanSave=false;throw new ArgumentException("Unsupported preferences version.");
    }
    return WidgetPreferences.FromNamed(root);
   }catch(Exception error){DiagnosticLog.Record(DiagnosticOperation.PreferencesLoad,error);return new WidgetPreferences();}
  }
  internal string Save(WidgetPreferences value) {
   if(!loaded)Load();
   if(!CanSave)throw new InvalidOperationException("Unsupported preferences must not be overwritten.");
   if(value==null||value.Version!=1)throw new ArgumentException("Invalid preferences version.");
   value.Normalize();
   string content=new JavaScriptSerializer().Serialize(value);
   if(legacy&&!File.Exists(path+".legacy.json"))AtomicFile.Write(path+".legacy.json",LoadedText);
   AtomicFile.Write(path,content);
   LoadedText=content;legacy=false;return content;
  }
 }
}
