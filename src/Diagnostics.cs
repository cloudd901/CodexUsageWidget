using System;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;

namespace CodexUsageBubble {
 internal enum DiagnosticOperation {
  PreferencesLoad, PreferencesSave, MonitorPlacementLoad, SettingsWrite, ActionSettingsLoad,
  UsageRead, ChatCatalogRead, ChatSend, ActionDispatch, ActionCompletion, ActionReceipt, ActionCancellation, TrayCreate
 }
 // Deliberately no string payload API: exceptions may contain private protocol data.
 internal static class DiagnosticLog {
  internal const int MaxFileBytes=128*1024;
  static readonly object gate=new object();
  static readonly Encoding encoding=new UTF8Encoding(false);
  internal static string FilePath {
   get {
    string settings=Environment.GetEnvironmentVariable("CODEX_USAGE_BUBBLE_SETTINGS");
    string directory=string.IsNullOrWhiteSpace(settings)?Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"CodexUsageBubble"):Path.GetDirectoryName(Path.GetFullPath(settings));
    return Path.Combine(directory,"diagnostics.log");
   }
  }
  internal static void Record(DiagnosticOperation operation,Exception error) {
   if(error==null||error is OperationCanceledException)return;
   try {
    string category=error is Win32Exception?"Win32":error is UnauthorizedAccessException?"AccessDenied":error is IOException?"IO":error is FormatException?"Format":error is ArgumentException?"Argument":error is TimeoutException?"Timeout":error is RpcException?"Protocol":error is UsageException?"Usage":error is AttentionException?"Interactive":"Unexpected";
    var native=error as Win32Exception;
    string entry=DateTime.UtcNow.ToString("o",CultureInfo.InvariantCulture)+" operation="+operation.ToString()+" category="+category+" hresult="+error.HResult.ToString("X8",CultureInfo.InvariantCulture)+(native==null?"":" native="+native.NativeErrorCode.ToString(CultureInfo.InvariantCulture))+Environment.NewLine;
    lock(gate) {
     string path=FilePath;
     Directory.CreateDirectory(Path.GetDirectoryName(path));
     if(File.Exists(path)&&new FileInfo(path).Length+encoding.GetByteCount(entry)>MaxFileBytes) {
      string previous=path+".1";
      if(File.Exists(previous))File.Delete(previous);
      File.Move(path,previous);
     }
     File.AppendAllText(path,entry,encoding);
    }
   }catch(Exception) {
    // Diagnostics must never change recovery, trigger a retry, or recurse on failure.
   }
  }
 }
}
