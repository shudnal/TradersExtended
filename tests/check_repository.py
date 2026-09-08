"""Source and packaging invariants only; does not compile or execute any C# code."""
from pathlib import Path
import json
import re
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parents[1]
ns = {'m': 'http://schemas.microsoft.com/developer/msbuild/2003'}
project = ET.parse(root / 'TradersExtended.csproj')
compiled = {entry.attrib['Include'].replace('\\', '/') for entry in project.findall('.//m:Compile', ns)}
actual = {str(path.relative_to(root)) for path in root.glob('*.cs')}
actual |= {str(path.relative_to(root)) for folder in ['Compatibility', 'Properties'] for path in (root / folder).rglob('*.cs')}
assert compiled == actual, (compiled - actual, actual - compiled)
assert project.find('.//m:LangVersion', ns).text == '11'
targets = ET.parse(root / 'ILRepack.targets')
for element in targets.findall('.//*[@Include]'):
    if element.tag.endswith('InputAssemblies'):
        assert 'ConditionalConfigSync' not in element.attrib['Include'], 'CCS must remain external'
readme = (root / 'README.md').read_text(encoding='utf-8-sig')
assert readme == (root / 'package/thunderstore/TradersExtended/README.md').read_text(encoding='utf-8-sig')
for link in ['https://buymeacoffee.com/shudnal', 'https://discord.gg/e3UtQB8GFK']:
    assert link in readme
blocks = re.findall(r'```json\s*\n(.*?)\n```', readme, re.S)
for block in blocks:
    json.loads(block)
full = next(json.loads(block) for block in blocks if 'Buyback lifetime in world seconds' in block)
assert sum(len(section) for section in full.values()) == 24
assert isinstance(full['Misc']['Fixed position for Store GUI'], dict)
assert not any('color' in name for name in full['Trader buyback'])
for path in root.rglob('*.json'):
    if not any(part in {'.git', 'bin', 'obj'} for part in path.parts):
        json.loads(path.read_text(encoding='utf-8-sig'))
text_files = 0
for path in root.rglob('*'):
    if not path.is_file() or any(part in {'.git', 'bin', 'obj', '.review-import'} for part in path.relative_to(root).parts):
        continue
    raw = path.read_bytes()
    if b'\x00' in raw:
        continue
    try:
        text = raw.decode('utf-8-sig')
    except UnicodeDecodeError:
        continue
    assert not re.search(r'[\u0400-\u04ff]', text), path
    text_files += 1
manifest = json.loads((root / 'package/thunderstore/TradersExtended/manifest.json').read_text())
assert not any('YamlDotNet' in dependency for dependency in manifest['dependencies'])
assert manifest['version_number'] == '2.0.1'
transport = (root / 'ConfigEditorTransport.cs').read_text()
assert 'InvokeRoutedRPC' not in transport and 'LocalPlayerIsAdminOrHost' not in transport
assert 'GetPeer(sender)' in transport and 'peer.m_socket.GetHostName()' in transport
assert 'WriteAtomically' in transport
print(f'PASS: project includes {len(compiled)} production C# files; {text_files} UTF-8 text files scanned; README, schemas, packaging and transport invariants verified.')
