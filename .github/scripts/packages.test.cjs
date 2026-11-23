const test = require('node:test');
const assert = require('node:assert/strict');
const fs = require('node:fs'), os = require('node:os'), path = require('node:path'), zlib = require('node:zlib');
const { traceMarker, projectPackage, expectedPackages, zipEntries, dependencies, siblingVersionProblems, checkPackage, checkAnalyzerPackage, check } = require('./packages.cjs');

// A zip archive of the entries, the even-indexed ones deflated and the rest stored.
function zip(entries) {
  const locals = [], centrals = [];
  let offset = 0;
  Object.entries(entries).forEach(([name, content], index) => {
    const raw = Buffer.from(content);
    const method = index % 2 === 0 ? 8 : 0;
    const data = method === 8 ? zlib.deflateRawSync(raw) : raw;
    const fileName = Buffer.from(name);
    const local = Buffer.alloc(30);
    local.writeUInt32LE(0x04034b50, 0);
    local.writeUInt16LE(method, 8);
    local.writeUInt32LE(data.length, 18);
    local.writeUInt32LE(raw.length, 22);
    local.writeUInt16LE(fileName.length, 26);
    const central = Buffer.alloc(46);
    central.writeUInt32LE(0x02014b50, 0);
    central.writeUInt16LE(method, 10);
    central.writeUInt32LE(data.length, 20);
    central.writeUInt32LE(raw.length, 24);
    central.writeUInt16LE(fileName.length, 28);
    central.writeUInt32LE(offset, 42);
    locals.push(local, fileName, data);
    centrals.push(central, fileName);
    offset += local.length + fileName.length + data.length;
  });
  const directory = Buffer.concat(centrals);
  const end = Buffer.alloc(22);
  end.writeUInt32LE(0x06054b50, 0);
  end.writeUInt16LE(centrals.length / 2, 8);
  end.writeUInt16LE(centrals.length / 2, 10);
  end.writeUInt32LE(directory.length, 12);
  end.writeUInt32LE(offset, 16);
  return Buffer.concat([...locals, directory, end]);
}

const project = `<Project><PropertyGroup>
  <TargetFrameworks>net10.0;net8.0</TargetFrameworks>
  <AssemblyName>Lib</AssemblyName>
  <PackageId>Lib</PackageId>
  <PackageId Condition="'$(RanvierTrace)' == 'true'">Lib.Traced</PackageId>
  <Version>0.2.0</Version>
</PropertyGroup></Project>`;

function nupkg(id, { version = '0.2.0', assembly = 'Lib', traced = id.endsWith('.Traced'), frameworks = ['net10.0', 'net8.0'], dependencies = {} } = {}) {
  const dll = Buffer.concat([Buffer.from('MZ'), traced ? traceMarker : Buffer.from('RanvierTrace'), Buffer.from('end')]);
  const group = Object.entries(dependencies).map(([dependency, range]) => `<dependency id="${dependency}" version="${range}" exclude="Build,Analyzers" />`).join('');
  const entries = { [`${id}.nuspec`]: `<package><metadata><id>${id}</id><version>${version}</version><dependencies><group targetFramework="net10.0">${group}</group></dependencies></metadata></package>` };
  for (const framework of frameworks) entries[`lib/${framework}/${assembly}.dll`] = dll;
  entries['fable/Lib.fsproj'] = '<Project />';
  return zip(entries);
}

const lib = projectPackage(project, 'Lib');
const expected = expectedPackages([lib]);
const untraced = expected.get('Lib.0.2.0.nupkg');
const traced = expected.get('Lib.Traced.0.2.0.nupkg');

test('a project yields its untraced and traced packages at its version', () => {
  assert.deepEqual(lib, { id: 'Lib', version: '0.2.0', assembly: 'Lib', frameworks: ['net10.0', 'net8.0'], exactSiblings: false });
  assert.deepEqual([...expected.keys()], ['Lib.0.2.0.nupkg', 'Lib.Traced.0.2.0.nupkg']);
  assert.equal(untraced.traced, false);
  assert.equal(traced.traced, true);
});
test('zip entries read back stored and deflated', () => {
  const entries = zipEntries(zip({ 'a.txt': 'deflated', 'b/c.txt': 'stored' }));
  assert.equal(entries.get('a.txt')().toString(), 'deflated');
  assert.equal(entries.get('b/c.txt')().toString(), 'stored');
});
test('matching packages have no problems', () => {
  assert.deepEqual(checkPackage(nupkg('Lib'), untraced), []);
  assert.deepEqual(checkPackage(nupkg('Lib.Traced'), traced), []);
});
test('a traced id over an untraced build fails, and the reverse', () => {
  assert.deepEqual(checkPackage(nupkg('Lib.Traced', { traced: false }), traced), [
    'Lib.Traced: lib/net10.0/Lib.dll is untraced',
    'Lib.Traced: lib/net8.0/Lib.dll is untraced',
  ]);
  assert.equal(checkPackage(nupkg('Lib', { traced: true }), untraced).length, 2);
});
test('a package depends on external Ranvier and Ranvier.Css.Core packages matching its trace build', () => {
  const ext = ['Ranvier', 'Ranvier.Css.Core'];
  assert.deepEqual(
    checkPackage(nupkg('Lib.Traced', { dependencies: { 'Ranvier.Traced': '0.2.1', 'Ranvier.Css.Core.Traced': '0.3.0' } }), traced, [lib], ext),
    []
  );
  assert.deepEqual(
    checkPackage(nupkg('Lib.Traced', { dependencies: { 'Ranvier.Traced': '0.2.1', 'Ranvier.Css.Core': '0.3.0' } }), traced, [lib], ext),
    ['Lib.Traced: depends on Ranvier.Css.Core, expected Ranvier.Css.Core.Traced']
  );
  assert.deepEqual(
    checkPackage(nupkg('Lib', { dependencies: { 'Ranvier': '0.2.1', 'Ranvier.Css.Core.Traced': '0.3.0' } }), untraced, [lib], ext),
    ['Lib: depends on Ranvier.Css.Core.Traced, expected Ranvier.Css.Core']
  );
});
test('native Ranvier.* projects inherit TargetFramework from Directory.Native.props', () => {
  const dir = fs.mkdtempSync(path.join(os.tmpdir(), 'anime-packages-'));
  const src = path.join(dir, 'src'), bin = path.join(dir, 'bin');
  try {
    fs.writeFileSync(path.join(dir, 'Directory.Native.props'), '<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>');
    fs.mkdirSync(path.join(src, 'Ranvier.Anime'), { recursive: true });
    fs.mkdirSync(bin);
    fs.writeFileSync(
      path.join(src, 'Ranvier.Anime', 'Ranvier.Anime.fsproj'),
      `<Project><PropertyGroup><AssemblyName>Ranvier.Anime</AssemblyName><PackageId>Ranvier.Anime</PackageId><PackageId Condition="'$(RanvierTrace)' == 'true'">Ranvier.Anime.Traced</PackageId><Version>0.3.0</Version></PropertyGroup><PropertyGroup Condition="'$(BlendBenchmark)' == 'true'"><IsPackable>false</IsPackable></PropertyGroup></Project>`
    );
    for (const id of ['Ranvier.Anime', 'Ranvier.Anime.Traced']) {
      fs.writeFileSync(path.join(bin, `${id}.0.3.0.nupkg`), nupkg(id, { version: '0.3.0', assembly: 'Ranvier.Anime', frameworks: ['net10.0'] }));
    }
    assert.deepEqual(check(bin, src), []);
  } finally {
    fs.rmSync(dir, { recursive: true, force: true });
  }
});
test('a nuspec lists each dependency once with its range', () => {
  const entries = zipEntries(nupkg('Lib', { dependencies: { Core: '[1.0.0]', 'FSharp.Core': '8.0.100' } }));
  const manifest = entries.get('Lib.nuspec')().toString();
  assert.deepEqual([...dependencies(manifest)], [['Core', '[1.0.0]'], ['FSharp.Core', '8.0.100']]);
});
test('exact-sibling projects must share one version', () => {
  const exactProject = (name, version) => projectPackage(project.replace(/Lib/g, name).replace('0.2.0', version)
    .replace('</PropertyGroup>', '<RanvierExactSiblingVersions>true</RanvierExactSiblingVersions></PropertyGroup>'), name);
  assert.deepEqual(siblingVersionProblems([exactProject('Lib', '0.2.0'), exactProject('Sibling', '0.2.0')]), []);
  assert.deepEqual(siblingVersionProblems([exactProject('Lib', '0.2.0'), exactProject('Sibling', '0.3.0')]), [
    'exact-sibling projects have different versions: Lib 0.2.0, Sibling 0.3.0',
  ]);
});
test('an analyzer package holds its assembly under analyzers/dotnet/fs and its build assets', () => {
  const exp = { id: 'Ranvier.Analyzers', version: '0.1.0', assembly: 'Ranvier.Analyzers', analyzer: true };
  const good = zip({
    'Ranvier.Analyzers.nuspec': '<package><metadata><id>Ranvier.Analyzers</id><version>0.1.0</version></metadata></package>',
    'analyzers/dotnet/fs/Ranvier.Analyzers.dll': 'x',
    'build/Ranvier.Analyzers.props': '<Project />',
    'build/Ranvier.Analyzers.targets': '<Project />',
  });
  assert.deepEqual(checkAnalyzerPackage(good, exp), []);
});
