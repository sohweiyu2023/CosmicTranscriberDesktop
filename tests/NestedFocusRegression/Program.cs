using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Documents;
using System.Windows.Markup;
using System.Windows.Media;
using System.Windows.Threading;
using System.Xml.Linq;
using CosmicTranscriber.Utilities;

internal static class Program
{
    private static int _checks;
    private static readonly HashSet<string> Events = ["Loaded","Closing","GotKeyboardFocus","PreviewKeyDown","Click","PasswordChanged","TextChanged","SelectionChanged","PreviewMouseWheel","ValueChanged","SizeChanged","ScrollChanged"];
    [STAThread]
    private static int Main(string[] args)
    {
        var source=Path.GetFullPath(args[0]);var output=Path.GetFullPath(args[1]);Directory.CreateDirectory(output);
        var app=new Application { ShutdownMode=ShutdownMode.OnExplicitShutdown };
        var results=new List<object>();
        foreach(var theme in new[]{"CosmicNight","CosmicPaper"})
        foreach(var size in new[]{(W:616d,H:700d,Dpi:216d),(W:1060d,H:568d,Dpi:216d),(W:1048d,H:538d,Dpi:216d),(W:1060d,H:700d,Dpi:144d)})
        {
            app.ThemeMode=theme=="CosmicPaper"?ThemeMode.Light:ThemeMode.Dark;
            app.Resources.MergedDictionaries.Clear();
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(File.ReadAllText(Path.Combine(source,$"src/CosmicTranscriber/Themes/{theme}.xaml"))));
            var shared=File.ReadAllText(Path.Combine(source,"src/CosmicTranscriber/Themes/SharedStyles.xaml")).Replace("clr-namespace:CosmicTranscriber.Models","clr-namespace:CosmicTranscriber.Models;assembly=NestedFocusRegression");
            app.Resources.MergedDictionaries.Add((ResourceDictionary)XamlReader.Parse(shared));
            var doc=XDocument.Load(Path.Combine(source,"src/CosmicTranscriber/MainWindow.xaml"));XNamespace x="http://schemas.microsoft.com/winfx/2006/xaml";
            foreach(var e in doc.Descendants())foreach(var a in e.Attributes().ToArray())if(a.Name==x+"Class" || a.Name.NamespaceName==""&&Events.Contains(a.Name.LocalName))a.Remove();
            var window=(Window)XamlReader.Parse(doc.ToString());var content=(FrameworkElement)window.Content;window.Content=null;
            TextElement.SetFontFamily(content,window.FontFamily);TextElement.SetForeground(content,window.Foreground);
            TextOptions.SetTextFormattingMode(content,TextOptions.GetTextFormattingMode(window));TextOptions.SetTextRenderingMode(content,TextOptions.GetTextRenderingMode(window));
            var outer=(ScrollViewer)window.FindName("WorkspaceViewport");var inner=(ScrollViewer)window.FindName("SettingsViewport");var target=(Button)window.FindName("OpenDiagnosticLogsButton");var log=(TextBox)window.FindName("LogTextBox");
            var root=new Border{Width=size.W,Height=size.H,Child=content,ClipToBounds=true,UseLayoutRounding=true,SnapsToDevicePixels=true};
            VisualTreeHelper.SetRootDpi(root,new DpiScale(size.Dpi/96,size.Dpi/96));
            log.Text="Synthetic focus QA only. No user files, keys, transcript or network.\n"+new string('x',150);
            foreach(var expander in Walk(content).OfType<Expander>())expander.IsExpanded=false;
            Pump(root,size.W,size.H);
            var activity=Walk(content).OfType<Expander>().Single(e=>Equals(e.Header,"Activity log"));
            Console.WriteLine("ACTUAL_FLUENT_FACTOR_PART="+(activity.Template?.FindName("AnimationFactorBorder",activity) is Border));
            activity.BringIntoView();Pump(root,size.W,size.H);activity.IsExpanded=true;
            log.BringIntoView();target.BringIntoView();Pump(root,size.W,size.H);
            var before=Fraction(target,root);IInputElement? focused=target;var traces=new List<FocusViewportTrace>();
            using(var correction=new FocusedViewportRevealer(inner,outer,traces.Add,()=>focused,e=>e.Visibility==Visibility.Visible))
            {
                correction.Attach(root);var ordinary=0;KeyboardFocusChangedEventHandler ordinaryHandler=(_,_)=>ordinary++;root.AddHandler(Keyboard.GotKeyboardFocusEvent,ordinaryHandler);
                target.RaiseEvent(new KeyboardFocusChangedEventArgs(Keyboard.PrimaryDevice,0,null,target){RoutedEvent=Keyboard.GotKeyboardFocusEvent,Handled=true});
                Pump(root,size.W,size.H);var after=Fraction(target,root);
                Check(ordinary==0 && traces.Any(t=>t.Stage=="handler_enter"&&t.EventHandled),"handled focus delivered to production listener");
                Check(traces.Count(t=>t.Stage=="requested")==2,"one request per ancestor, no retries");
                Check(traces.Any(t=>t.Stage=="complete"&&t.ViewportId=="WorkspaceViewport"),"outer transaction completed");
                Check(after>=.98,$"source layout target visible {theme} {size.W}x{size.H} {size.Dpi}dpi (fraction {after})");
                Check(Math.Abs(VisualTreeHelper.GetDpi(root).PixelsPerInchY-size.Dpi)<.01,"actual layout DPI set, not raster-only");
                results.Add(new{theme,width=size.W,height=size.H,dpi=size.Dpi,before,after,traces});
                root.RemoveHandler(Keyboard.GotKeyboardFocusEvent,ordinaryHandler);
            }
            root.Child=null;window.Close();
        }
        // Deterministic nested-clipping control: inner visibility alone cannot reveal through outer.
        foreach(var innerOnly in new[]{true,false})
        {
            var target=new Button{Name="Target",Content="Synthetic diagnostic control",Width=160,Height=38,HorizontalAlignment=HorizontalAlignment.Left};
            var innerStack=new StackPanel();innerStack.Children.Add(new Border{Height=350});innerStack.Children.Add(target);innerStack.Children.Add(new Border{Height=20});
            var inner=new ScrollViewer{Name="SettingsViewport",Height=160,Content=innerStack,CanContentScroll=false};
            var outerStack=new StackPanel();outerStack.Children.Add(new Border{Height=160});outerStack.Children.Add(inner);outerStack.Children.Add(new Border{Height=100});
            var outer=new ScrollViewer{Name="WorkspaceViewport",Content=outerStack,CanContentScroll=false};
            var root=new Border{Width=400,Height=180,Child=outer,ClipToBounds=true};VisualTreeHelper.SetRootDpi(root,new DpiScale(2.25,2.25));Pump(root,400,180);
            IInputElement? focused=target;var traces=new List<FocusViewportTrace>();
            using var correction=new FocusedViewportRevealer(inner,innerOnly?inner:outer,traces.Add,()=>focused,e=>e.Visibility==Visibility.Visible);
            correction.Request(target);Pump(root,400,180);var visible=Fraction(target,root);
            Check(innerOnly?visible<.98:visible>=.98,innerOnly?"negative control: inner-only leaves ancestor clipping":"nested control: both ancestors reveal target");
            if(!innerOnly)
            {
                traces.Clear();correction.Request(target);focused=null;Pump(root,400,180);Check(!traces.Any(t=>t.Stage=="requested"),"changed focus cancels queued work");
                focused=target;traces.Clear();correction.Attach(root);correction.Request(target);target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,120){RoutedEvent=Mouse.PreviewMouseWheelEvent});Pump(root,400,180);Check(!traces.Any(t=>t.Stage=="requested"),"user wheel cancels pending correction");
                inner.ScrollToVerticalOffset(0);Pump(root,400,180);Check(inner.VerticalOffset==0,"completed transaction does not fight manual scrolling");
                traces.Clear();correction.Request(target);correction.Dispose();Pump(root,400,180);Check(!traces.Any(t=>t.Stage=="requested"),"dispose cancels queued work");
            }
            results.Add(new{fixture="synthetic nested viewport",innerOnly,visible,traces});root.Child=null;
        }
        // Deterministic delayed expansion analogue; actual input/render remains a native gate.
        foreach(var size in new[]{(W:616d,H:700d),(W:1060d,H:568d)})
        foreach(var observe in new[]{false,true})
        {
            var target=new Button{Name="LateTarget",Content="Synthetic diagnostics",Width=160,Height=38};
            var preceding=new Border{Height=900};var stack=new StackPanel();stack.Children.Add(preceding);stack.Children.Add(target);stack.Children.Add(new Border{Height=100});
            var expander=new Expander{IsExpanded=true,Content=stack,Template=(ControlTemplate)XamlReader.Parse("<ControlTemplate xmlns='http://schemas.microsoft.com/winfx/2006/xaml/presentation' xmlns:x='http://schemas.microsoft.com/winfx/2006/xaml' TargetType='Expander'><Grid><Border x:Name='AnimationFactorBorder' Width='1' Visibility='Collapsed'/><ContentPresenter Content='{TemplateBinding Content}'/></Grid></ControlTemplate>")};
            var inner=new ScrollViewer{Name="SettingsViewport",Height=180,Content=expander,CanContentScroll=false};
            var outside=new StackPanel();outside.Children.Add(new Border{Height=size.H-100});outside.Children.Add(inner);outside.Children.Add(new Border{Height=100});
            var outer=new ScrollViewer{Name="WorkspaceViewport",Content=outside,CanContentScroll=false};
            var root=new Border{Width=size.W,Height=size.H,Child=outer,ClipToBounds=true};VisualTreeHelper.SetRootDpi(root,new DpiScale(2.25,2.25));Pump(root,size.W,size.H);
            var factor=(Border)expander.Template.FindName("AnimationFactorBorder",expander);double clock=0;
            var traces=new List<FocusViewportTrace>();IInputElement? focused=target;
            using var correction=new FocusedViewportRevealer(inner,outer,traces.Add,()=>focused,e=>true,observe,false,()=>clock);
            correction.Attach(root);correction.Request(target);Pump(root,size.W,size.H);Check(Fraction(target,root)>=.98,"delayed fixture initially revealed");
            correction.ObservePostRender();
            // A quiet plateau exceeds the stability interval, but a running expansion must prevent early finish.
            clock=100;correction.ObservePostRender();Pump(root,size.W,size.H);
            foreach(var step in new[]{(Time:150d,Delta:12d,Factor:.7),(Time:270d,Delta:16d,Factor:.25),(Time:333d,Delta:12d,Factor:0d)})
            {clock=step.Time;factor.Width=step.Factor;preceding.Height+=step.Delta;Pump(root,size.W,size.H);correction.ObservePostRender();Pump(root,size.W,size.H);}
            var shifted=Fraction(target,root);Check(shifted<.98,"negative control: delayed motion clips first arrival after four early frames");
            clock=420;correction.ObservePostRender();Pump(root,size.W,size.H);clock=510;correction.ObservePostRender();Pump(root,size.W,size.H);clock=600;correction.ObservePostRender();Pump(root,size.W,size.H);
            var post=Fraction(target,root);Check(observe?post>=.98:post<.98,observe?"stability correction reveals unchanged focus after expansion ends":"one-pass negative remains clipped until refocus");
            if(!observe){correction.Request(target);Pump(root,size.W,size.H);Check(Fraction(target,root)>=.98,"negative control refocus recovery");}
            else
            {
                Check(traces.Any(t=>t.Stage=="stable_visible"),"stable geometry and visibility terminate observer");
                var count=traces.Count;clock=700;correction.ObservePostRender();Check(traces.Count==count,"stable observer unsubscribed");
                clock=0;factor.Width=1;correction.Request(target);Pump(root,size.W,size.H);traces.Clear();var startOffset=inner.VerticalOffset;
                for(var i=0;i<22;i++){clock=i*50;preceding.Height+=2;Pump(root,size.W,size.H);correction.ObservePostRender();}
                var stoppedCount=traces.Count;clock=1200;correction.ObservePostRender();Pump(root,size.W,size.H);
                Check(inner.VerticalOffset==startOffset&&!traces.Any(t=>t.Stage=="requested")&&traces.Count==stoppedCount,"continuous motion terminates at absolute deadline without scrolling loop");
                foreach(var cancel in new[]{"wheel","pointer","scroll_key","focus","deadline","dispose"})
                {
                    clock=0;factor.Width=0;focused=target;correction.Request(target);Pump(root,size.W,size.H);correction.ObservePostRender();
                    preceding.Height+=26;Pump(root,size.W,size.H);clock=100;correction.ObservePostRender();clock=200;correction.ObservePostRender(); // queues stable late correction, no dispatcher drain yet
                    var offset=inner.VerticalOffset;traces.Clear();
                    if(cancel=="wheel")target.RaiseEvent(new MouseWheelEventArgs(Mouse.PrimaryDevice,0,120){RoutedEvent=Mouse.PreviewMouseWheelEvent});
                    if(cancel=="pointer")target.RaiseEvent(new MouseButtonEventArgs(Mouse.PrimaryDevice,0,MouseButton.Left){RoutedEvent=Mouse.PreviewMouseDownEvent});
                    if(cancel=="scroll_key")correction.CancelForScrollKey(Key.PageDown);
                    if(cancel=="focus")focused=null;
                    if(cancel=="deadline")clock=1001;
                    if(cancel=="dispose")correction.Dispose();
                    Pump(root,size.W,size.H);Check(inner.VerticalOffset==offset&&!traces.Any(t=>t.Stage=="requested"),"queued stable correction cancelled: "+cancel);
                }
            }
            results.Add(new{fixture="delayed expansion factor / unchanged injected focus, explicit render samples",size,observe,shifted,post,traces});root.Child=null;
        }
        File.WriteAllText(Path.Combine(output,"nested-focus-results.json"),JsonSerializer.Serialize(new{checks=_checks,mode="Off-screen WPF source layout/routed events; no HWND/Window.Show or OS input. Injected focus provider is test-only.",results},new JsonSerializerOptions{WriteIndented=true}));
        app.Shutdown();Console.WriteLine("NESTED_FOCUS_CHECKS="+_checks);return 0;
    }
    private static void Check(bool condition,string name){if(!condition)throw new InvalidOperationException("FAIL "+name);_checks++;Console.WriteLine("PASS "+name);}
    private static void Pump(FrameworkElement root,double w,double h){for(var i=0;i<6;i++){root.Measure(new Size(w,h));root.Arrange(new Rect(0,0,w,h));root.UpdateLayout();root.Dispatcher.Invoke(()=>{},DispatcherPriority.ApplicationIdle);root.UpdateLayout();}}
    private static IEnumerable<DependencyObject> Walk(DependencyObject element){yield return element;for(var i=0;i<VisualTreeHelper.GetChildrenCount(element);i++)foreach(var child in Walk(VisualTreeHelper.GetChild(element,i)))yield return child;}
    private static double Fraction(FrameworkElement target,FrameworkElement root)
    {
        var bounds=target.TransformToAncestor(root).TransformBounds(new Rect(target.RenderSize));var visible=Rect.Intersect(bounds,new Rect(root.RenderSize));
        for(DependencyObject? d=target;d is not null&&d!=root;d=VisualTreeHelper.GetParent(d))
        {
            if(d is Visual v){var clip=VisualTreeHelper.GetClip(v);if(clip is not null)visible.Intersect(v.TransformToAncestor(root).TransformBounds(clip.Bounds));if(d is ScrollContentPresenter||d is UIElement{ClipToBounds:true}ui)visible.Intersect(v.TransformToAncestor(root).TransformBounds(new Rect(((UIElement)d).RenderSize)));}
        }
        return bounds.Width*bounds.Height>0&&!visible.IsEmpty?visible.Width*visible.Height/(bounds.Width*bounds.Height):0;
    }
}

