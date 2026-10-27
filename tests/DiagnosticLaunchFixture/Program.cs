using CosmicTranscriber.Utilities;
var mode=Environment.GetEnvironmentVariable("COSMIC_DIAG_FIXTURE_MODE")??"failure";
if(mode=="failure"){
    Console.Error.Write("System.BadImageFormatException: PRIVATE_AUDIO_TRANSCRIPT_SECRET sk-test-only "+new string('x',20000));
    Console.Write(new string('y',30000));return 73;
}
StartupDiagnostics.Initialize();StartupDiagnostics.WriteLifecycle("fixture_ready");
if(mode=="wait"){Thread.Sleep(30000);}
StartupDiagnostics.Shutdown(0);return 0;

