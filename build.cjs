const fs = require('node:fs');
const path = require('node:path');
const { spawnSync } = require('node:child_process');

function build({ component = 'all', output = path.join(__dirname, 'build'), test = false, testOutput = path.join(__dirname, 'build/tests') } = {}) {
  if (!['all', 'key-helper', 'tray-host'].includes(component)) throw Error('Unknown Windows helper: ' + component);
  output = path.resolve(output);
  testOutput = path.resolve(testOutput);
  const compiler = path.join(process.env.WINDIR || 'C:/Windows', 'Microsoft.NET/Framework64/v4.0.30319/csc.exe');
  if (!fs.existsSync(compiler)) throw Error('Windows x64 with .NET Framework 4.8 is required to build the helpers');
  fs.mkdirSync(output, { recursive: true });
  function compile(name, target, source, extra = []) {
    const result = spawnSync(compiler, ['/nologo', '/target:' + target, '/platform:x64', '/optimize+', '/utf8output', '/codepage:65001',
      '/reference:System.Web.Extensions.dll', '/out:' + path.resolve(output, name), path.join(__dirname, source), ...extra], { encoding: 'utf8', windowsHide: true });
    if (result.error || result.status !== 0) throw result.error || Error(result.stdout || result.stderr || 'Windows helper build failed');
  }
  if (component !== 'tray-host') {
    compile('QaqrKeyHelper.exe', 'exe', 'key-helper/Program.cs');
    if (test) {
      fs.mkdirSync(testOutput, { recursive: true });
      const executable = path.join(testOutput, 'KeyHelperTests.exe');
      compile(executable, 'exe', 'key-helper/Program.cs', ['/main:QaqrKeyHelper.CoreTests', path.join(__dirname, 'key-helper/tests/CoreTests.cs')]);
      const result = spawnSync(executable, [], { encoding: 'utf8', windowsHide: true, timeout: 10000 });
      process.stdout.write(result.stdout || '');
      if (result.error || result.status !== 0) throw result.error || Error(result.stderr || 'Keyboard helper tests failed');
    }
  }
  if (component !== 'key-helper') compile('QaqrTrayHost.exe', 'winexe', 'tray-host/Program.cs', ['/reference:System.Windows.Forms.dll', '/reference:System.Drawing.dll']);
  fs.copyFileSync(path.join(__dirname, 'LICENSE'), path.join(output, 'LICENSE'));
  console.log('Built Windows helpers: ' + component);
}

if (require.main === module) build({ test: process.argv.includes('--test') });
module.exports = { build };
