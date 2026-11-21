using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
namespace CodexUsageBubble {
 public sealed class CodexClient {
  // Only the registered Store package is cached. Loose binaries are rechecked.
  static string cached;
  const string DiscoveryScript=@"[Console]::OutputEncoding=[Text.Encoding]::UTF8
$package = Appx\Get-AppxPackage -Name OpenAI.Codex -ErrorAction SilentlyContinue |
    Where-Object { $_.PackageFamilyName -eq 'OpenAI.Codex_2p2nqsd0c76g0' -and $_.SignatureKind -eq 'Store' }
foreach ($item in $package) {
    $file = Join-Path $item.InstallLocation 'app\resources\codex.exe'
    if ([IO.File]::Exists($file)) { 'package|' + $file; exit }
}
foreach ($folder in ($env:CODEX_WIDGET_DISCOVERY_PATH -split ';')) {
    if (!$folder) { continue }
    $file = [IO.Path]::Combine($folder, 'codex.exe')
    if (![IO.File]::Exists($file)) { continue }
    $signature = Microsoft.PowerShell.Security\Get-AuthenticodeSignature -LiteralPath $file -ErrorAction SilentlyContinue
    if ($signature.Status -eq 'Valid' -and $signature.SignerCertificate.GetNameInfo('SimpleName', $false) -eq 'OpenAI OpCo, LLC') {
        'signed|' + $file
        exit
    }
}";
  public static string Locate() {return Locate(CancellationToken.None);}
  public static string Locate(CancellationToken cancel) {
   cancel.ThrowIfCancellationRequested();
   string existing=cached;
   if(IsLocalAbsolutePath(existing)&&File.Exists(existing))return existing;
   string system=Environment.GetFolderPath(Environment.SpecialFolder.System);
   string shell=Path.Combine(system,@"WindowsPowerShell\v1.0\powershell.exe");
   bool registeredPackage;
   string path=Locate(cancel,shell,Environment.GetEnvironmentVariable("PATH"),out registeredPackage);
   if(registeredPackage)cached=path;
   return path;
  }
  internal static bool IsLocalAbsolutePath(string path) {
   if(string.IsNullOrEmpty(path)||path.Length<3)return false;
   char drive=char.ToUpperInvariant(path[0]);
   return drive>='A'&&drive<='Z'&&path[1]==':'&&(path[2]=='\\'||path[2]=='/');
  }
  internal static string DiscoveryFolders(string path) {
   var folders=new System.Collections.Generic.List<string>();
   foreach(string entry in (path??"").Split(';')) {
    string folder=entry.Trim().Trim('"');
    if(!IsLocalAbsolutePath(folder))continue;
    try {folders.Add(Path.GetFullPath(folder));}
    catch(ArgumentException){}
    catch(NotSupportedException){}
    catch(PathTooLongException){}
   }
   return string.Join(";",folders);
  }
  internal static string Locate(CancellationToken cancel,string shell,string path,out bool registeredPackage) {
   cancel.ThrowIfCancellationRequested();
   registeredPackage=false;
   try {
    string command=Convert.ToBase64String(System.Text.Encoding.Unicode.GetBytes(DiscoveryScript));
    var psi=new ProcessStartInfo(shell,"-NoProfile -NonInteractive -EncodedCommand "+command) {
     UseShellExecute=false,CreateNoWindow=true,RedirectStandardOutput=true,RedirectStandardError=true,
     WorkingDirectory=Path.GetDirectoryName(shell),StandardOutputEncoding=System.Text.Encoding.UTF8
    };
    psi.EnvironmentVariables["PSModulePath"]=Path.Combine(psi.WorkingDirectory,"Modules");
    psi.EnvironmentVariables["CODEX_WIDGET_DISCOVERY_PATH"]=DiscoveryFolders(path);
    var output=ReadDiscoveryOutput(psi,cancel);
    foreach(var line in output.Split(new[]{'\r','\n'},StringSplitOptions.RemoveEmptyEntries)) {
     cancel.ThrowIfCancellationRequested();
     int separator=line.IndexOf('|');
     if(separator<0)continue;
     string kind=line.Substring(0,separator);
     if(kind!="package"&&kind!="signed")continue;
     string candidate=line.Substring(separator+1);
     if(!IsLocalAbsolutePath(candidate)||!string.Equals(Path.GetFileName(candidate),"codex.exe",StringComparison.OrdinalIgnoreCase)||!File.Exists(candidate))continue;
     registeredPackage=kind=="package";
     return candidate;
    }
   }catch(OperationCanceledException){cancel.ThrowIfCancellationRequested();}
   catch(Exception){cancel.ThrowIfCancellationRequested();}
   cancel.ThrowIfCancellationRequested();
   throw new UsageException("Trusted Codex executable not found. Install Codex from the Microsoft Store or use an OpenAI-signed codex.exe on PATH, then Refresh now.");
  }
  static string ReadDiscoveryOutput(System.Diagnostics.ProcessStartInfo startInfo,CancellationToken cancel) {
   using(var budget=CancellationTokenSource.CreateLinkedTokenSource(cancel))
   using(var p=new System.Diagnostics.Process()) {
    budget.CancelAfter(10000);var token=budget.Token;p.StartInfo=startInfo;
    try {
     token.ThrowIfCancellationRequested();p.Start();
     using(token.Register(delegate {Kill(p);})) {
      token.ThrowIfCancellationRequested();
      p.ErrorDataReceived+=delegate {};p.BeginErrorReadLine();
      // Waiting on the asynchronous pipe read makes an open stdout cancellable.
      var output=p.StandardOutput.ReadToEndAsync();
      output.ContinueWith(delegate(System.Threading.Tasks.Task<string> completed){var ignored=completed.Exception;},System.Threading.Tasks.TaskContinuationOptions.OnlyOnFaulted);
      output.Wait(token);
      while(!p.WaitForExit(50))token.ThrowIfCancellationRequested();
      token.ThrowIfCancellationRequested();
      if(p.ExitCode!=0)throw new IOException("Trusted Codex discovery failed.");
      return output.GetAwaiter().GetResult();
     }
    }finally {Kill(p);try{p.WaitForExit(2000);}catch(InvalidOperationException){} }
   }
  }
  static void Kill(System.Diagnostics.Process p) {try{if(!p.HasExited)p.Kill();}catch(InvalidOperationException){}catch(System.ComponentModel.Win32Exception){} }
  public static UsageSnapshot Read(CancellationToken cancel) {return Read(cancel,Locate);}
  internal static UsageSnapshot Read(CancellationToken cancel,Func<CancellationToken,string> locate) {
   using(var deadline=CancellationTokenSource.CreateLinkedTokenSource(cancel)) {
    deadline.CancelAfter(30000);
    try {
     using(var server=new AppServer(locate(deadline.Token),deadline.Token)) {
      var result=server.Request("account/rateLimits/read",null);
      return UsageSnapshot.Parse(new System.Web.Script.Serialization.JavaScriptSerializer().Serialize(new {result=result}));
     }
    }catch(OperationCanceledException){if(cancel.IsCancellationRequested)throw;throw new UsageException("Codex usage read timed out (30 seconds). Check your network and Codex sign-in.");}
    catch(UsageException){throw;}
    catch(Exception){
     cancel.ThrowIfCancellationRequested();
     if(deadline.IsCancellationRequested)throw new UsageException("Codex usage read timed out (30 seconds). Check your network and Codex sign-in.");
     throw new UsageException("Could not read Codex App Server status. Check Codex sign-in and network, then Refresh now.");
    }
   }
  }
 }
}