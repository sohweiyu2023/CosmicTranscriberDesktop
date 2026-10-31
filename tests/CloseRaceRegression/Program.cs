using System.ComponentModel;
int checks=0;
void Check(bool value,string name){if(!value)throw new Exception("FAIL "+name);checks++;Console.WriteLine("PASS "+name);}
for(int flag=0;flag<4;flag++) {
 var finished=new TestWindow();finished.SetBusy(flag,true);
 MessageBox.Response=MessageBoxResult.Yes;MessageBox.DuringConfirmation=()=>finished.SetBusy(flag,false);
 var e=finished.RequestClose();
 Check(!e.Cancel&&finished.Persisted==1&&finished.FinalCloseAllowed&&!finished.Deferred,"completed during confirmation closes without orphaned defer "+flag);
 Check(!finished.LifetimeCancelled,"already completed work does not need cancellation "+flag);
 var active=new TestWindow();active.SetBusy(flag,true);MessageBox.DuringConfirmation=null;
 e=active.RequestClose();
 Check(e.Cancel&&active.Deferred&&active.LifetimeCancelled&&active.Persisted==0,"still-active work defers and cancels safely "+flag);
 var declined=new TestWindow();declined.SetBusy(flag,true);MessageBox.Response=MessageBoxResult.No;
 MessageBox.DuringConfirmation=()=>declined.SetBusy(flag,false);e=declined.RequestClose();
 Check(e.Cancel&&declined.Persisted==0&&!declined.LifetimeCancelled&&!declined.Deferred,"decline preserves window even if work finished "+flag);
 var replaced=new TestWindow();replaced.SetBusy(flag,true);MessageBox.Response=MessageBoxResult.Yes;
 MessageBox.DuringConfirmation=()=>{replaced.SetBusy(flag,false);replaced.SetBusy((flag+1)%4,true);};e=replaced.RequestClose();
 Check(e.Cancel&&replaced.Deferred,"different operation becoming active still defers "+flag);
}
MessageBox.DuringConfirmation=null;
var idle=new TestWindow();var idleClose=idle.RequestClose();Check(!idleClose.Cancel&&idle.Persisted==1,"ordinary idle close persists once");
Console.WriteLine($"{checks} close-race checks PASS; extracted actual handler, no WPF/network/profile access");
enum MessageBoxResult{Yes,No} enum MessageBoxButton{YesNo} enum MessageBoxImage{Warning}
static class MessageBox {
 internal static Action? DuringConfirmation;internal static MessageBoxResult Response;
 internal static MessageBoxResult Show(object owner,string text,string title,MessageBoxButton buttons,MessageBoxImage image){DuringConfirmation?.Invoke();return Response;}
}
static class StartupDiagnostics{internal static void Write(string category,string text) {}}
partial class TestWindow {
 bool _constructionCompleted=true,_allowFinalClose,_isRunning,_isImporting,_isTestingKey,_supportExportActive,_isClosing,_closeRequestedAfterUnwind;
 readonly CancellationTokenSource _lifetimeCancellation=new();readonly CancellationTokenSource? _runCancellation=null;
 internal int Persisted;internal bool FinalCloseAllowed=>_allowFinalClose;internal bool Deferred=>_closeRequestedAfterUnwind;internal bool LifetimeCancelled=>_lifetimeCancellation.IsCancellationRequested;
 internal void SetBusy(int flag,bool value){switch(flag){case 0:_isRunning=value;break;case 1:_isImporting=value;break;case 2:_isTestingKey=value;break;case 3:_supportExportActive=value;break;default:throw new ArgumentOutOfRangeException(nameof(flag));}}
 internal CancelEventArgs RequestClose(){var e=new CancelEventArgs();Window_Closing(this,e);return e;}
 void PersistBeforeFinalClose(){Persisted++;}
}
