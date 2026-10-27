using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using CosmicTranscriber.Models;
using CosmicTranscriber.Services;

var root=Path.GetFullPath(args[0]);Directory.CreateDirectory(root);int passed=0;
void Check(bool ok,string name){if(!ok)throw new Exception("FAIL "+name);passed++;Console.WriteLine("PASS "+name);}
// Exercise production load/save/normalization in an explicit synthetic directory.
// Bypass only the resolver constructor: no live KnownFolder/profile/vault is touched by this fixture.
var service=(AppSettingsService)RuntimeHelpers.GetUninitializedObject(typeof(AppSettingsService));
typeof(AppSettingsService).GetField("<SettingsDirectory>k__BackingField",BindingFlags.Instance|BindingFlags.NonPublic)!.SetValue(service,root);
Check(service.SettingsDirectory==root,"fixture stays in explicit workspace directory");
var original=new AppSettings{SettingsSchemaVersion=1,Model="gpt-4o-transcribe",CustomPricePerMinute=.006,CombinedTitle="Synthetic title",Prompt="Synthetic test context",OutputDirectory=Path.Combine(root,"output")};
File.WriteAllText(service.SettingsPath,JsonSerializer.Serialize(original));var loaded=service.Load();
Check(loaded.SettingsSchemaVersion==4,"legacy schema normalized to current4");
Check(loaded.Model=="gpt-transcribe"&&loaded.CustomPricePerMinute==.0045,"untouched legacy defaults migrate together");
Check(loaded.CombinedTitle==original.CombinedTitle&&loaded.OutputDirectory==original.OutputDirectory,"user title/output retained");
service.Save(loaded,false);var bytes=File.ReadAllBytes(service.SettingsPath);var reloaded=service.Load();service.Save(reloaded,false);
Check(bytes.SequenceEqual(File.ReadAllBytes(service.SettingsPath)),"save/reload is stable and idempotent");
Check(Directory.GetFiles(root,"*.tmp").Length==0,"atomic settings save leaves no temporary files");
var custom=new AppSettings{SettingsSchemaVersion=1,Model="gpt-4o-transcribe",CustomPricePerMinute=.123};File.WriteAllText(service.SettingsPath,JsonSerializer.Serialize(custom));var customLoaded=service.Load();
Check(customLoaded.Model==custom.Model&&customLoaded.CustomPricePerMinute==.123,"custom legacy model/price preserved");
File.WriteAllText(service.SettingsPath,"{invalid");Check(service.Load().Model=="gpt-transcribe","malformed settings fall back safely");
File.WriteAllText(service.SettingsPath,new string('x',1024*1024+1));Check(service.Load().Model=="gpt-transcribe","oversize settings bounded and rejected");
var vault=new ApiKeyStore(root);const string synthetic="test-vault-sentinel";
vault.SaveProtected(synthetic);Check(vault.HasProtectedKey&&vault.LoadProtected()==synthetic,"synthetic DPAPI CurrentUser key roundtrip");
Check(!Encoding.UTF8.GetString(File.ReadAllBytes(Path.Combine(root,"openai-key.protected"))).Contains(synthetic),"vault bytes do not contain plaintext sentinel");
service.Save(new AppSettings(),false);Check(!File.ReadAllText(service.SettingsPath).Contains(synthetic),"settings JSON never contains key sentinel");
vault.ForgetProtected();Check(!vault.HasProtectedKey,"forget removes only synthetic vault");
Console.WriteLine("NORMAL_SETTINGS_ASSERTIONS="+passed);

