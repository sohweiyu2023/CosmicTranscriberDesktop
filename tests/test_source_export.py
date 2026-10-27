from pathlib import Path
import sys
import unittest
import tempfile
import subprocess
sys.path.insert(0,str(Path(__file__).resolve().parents[1]/'scripts'))
from verify_source_export import audit_file, audit_history

class ExportBoundaryTests(unittest.TestCase):
    def test_source_text_allowed(self):
        self.assertEqual(audit_file('src/Example.cs',b'// Original code\nclass Example {}'),[])
    def test_runtime_binary_path_blocked(self):
        self.assertTrue(any('Forbidden' in p for p in audit_file('bin/Example.dll',b'MZ')))
    def test_withheld_artwork_blocked(self):
        self.assertTrue(any('Forbidden' in p for p in audit_file('src/Assets/logo.ico',b'example')))
    def test_profile_and_log_blocked(self):
        for name in ('settings.json','logs/session.jsonl','openai-key.protected','sample.transcript.txt','.env.local'):
            self.assertTrue(any('Forbidden' in p for p in audit_file(name,b'example')),name)
    def test_credential_value_not_printed(self):
        secret=('sk'+'-proj-'+'x'*60).encode()
        findings=audit_file('src/bad.cs',secret)
        self.assertTrue(any('Credential-pattern' in p for p in findings))
        self.assertNotIn(secret.decode(),'\n'.join(findings))
    def test_private_key_marker_blocked(self):
        marker=('-----BEGIN '+'PRIVATE KEY-----').encode()
        self.assertTrue(any('Credential-pattern' in p for p in audit_file('bad.txt',marker)))
    def test_embedded_image_blocked(self):
        image=('data:image/'+'png;base64,AAAA').encode()
        self.assertTrue(any('Embedded image' in p for p in audit_file('docs/preview.html',image)))
    def test_unknown_binary_and_rtf_blocked(self):
        self.assertTrue(audit_file('payload.dat',b'\x00\x01'))
        self.assertTrue(any('Unreviewed RTF' in p for p in audit_file('docs/unreviewed.rtf',b'{\\rtf1 example}')))
    def test_python_cache_blocked(self):
        self.assertTrue(any('Forbidden' in p for p in audit_file('scripts/__pycache__/module.pyc',b'example')))

class HistoryBoundaryTests(unittest.TestCase):
    def setUp(self):
        self.temp=tempfile.TemporaryDirectory(prefix='cosmic-source-history-')
        self.root=Path(self.temp.name).resolve()
        self.assertTrue(self.root.is_relative_to(Path(tempfile.gettempdir()).resolve()))
        self.git('init','--initial-branch=main','--quiet')
        self.git('config','user.name','Source export regression')
        self.git('config','user.email','source-export-tests@example.invalid')
        self.git('config','commit.gpgsign','false')
        hooks=self.root/'empty-hooks';hooks.mkdir()
        self.git('config','core.hooksPath',str(hooks))
        (self.root/'source.txt').write_text('Synthetic source fixture',encoding='utf-8')
        self.commit('Clean synthetic root')
    def tearDown(self):self.temp.cleanup()
    def git(self,*args):
        result=subprocess.run(['git','-C',str(self.root),*args],capture_output=True)
        self.assertEqual(result.returncode,0,result.stderr.decode(errors='replace'))
        return result.stdout
    def commit(self,message):
        self.git('add','-A');self.git('commit','--quiet','-m',message)
    def test_deleted_payload_remains_detectable_in_history(self):
        bad=self.root/'recording.mp3';bad.write_bytes(b'Synthetic, not audio')
        self.commit('Synthetic forbidden payload')
        bad.unlink();self.commit('Remove synthetic payload from HEAD')
        problems,commits,_=audit_history(self.root)
        self.assertEqual(commits,3)
        self.assertTrue(any('recording.mp3' in p for p in problems))
    def test_nonmain_reachable_secret_is_detected(self):
        self.git('checkout','-b','synthetic-side','--quiet')
        (self.root/'sample.txt').write_text('sk'+'-proj-'+'x'*60,encoding='utf-8')
        self.commit('Synthetic side fixture')
        self.git('checkout','main','--quiet')
        problems,commits,_=audit_history(self.root)
        self.assertEqual(commits,2)
        self.assertTrue(any('Credential-pattern' in p for p in problems))

if __name__=='__main__':unittest.main()
