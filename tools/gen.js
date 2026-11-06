// Generates catalog, resx and data from the prototype HTML.
const fs = require('fs'), vm = require('vm'), path = require('path');
const html = fs.readFileSync(process.argv[2], 'utf8');
const out = process.argv[3];
const get = (a, b) => { const i = html.indexOf(a), j = html.indexOf(b, i); if (i < 0 || j < 0) throw new Error('marker ' + a); return html.slice(i, j); };

// --- evaluate TR, CTL, PROF, GM, GI, D-strings in a sandbox
const ctx = { out: {} };
vm.createContext(ctx);
const trSrc = get('var TR=', '\nfunction R(');
const ctlSrc = get('var CTL=[', 'var BY={}');
const profSrc = get('var PROF=[', '\nPROF.forEach');
const gmSrc = get('var GM=[', '\nvar GL=');
const rtSrc = get('var RT={', '\nfunction biosPath');
const hwsSrc = get('var HWS=[', 'var HW=HWS[0]');
const dSrc = html.slice(html.indexOf('var LG='), html.indexOf('/* ---------- hardware'));
vm.runInContext('var LG="ru";var D={ru:{},ua:{},en:{}};' + trSrc + ';' + ctlSrc + ';' + profSrc + ';' + gmSrc + ';' + rtSrc + ';' + hwsSrc +
  ';function T(x){x.split("\\n").forEach(function(l){var a=l.split("|");if(a.length<4)return;D.ru[a[0]]=a[1];D.ua[a[0]]=a[2];D.en[a[0]]=a[3]})}' +
  ';function T2(x){x.split("\\n").forEach(function(l){var a=l.split("|");if(a.length<4)return;D.en[a[0]]=a[1];D.ru[a[0]]=a[2];D.ua[a[0]]=a[3]})}', ctx);
// T(...) calls and D.xx assignments (not the function definitions)
const calls = [...html.matchAll(/^T2?\("[\s\S]*?"\);/gm)].map(m => m[0]);
vm.runInContext(calls.join('\n'), ctx);
const dAssign = [...html.matchAll(/^D\.(ru|ua|en)\.\w+=(['"])[\s\S]*?\2;$/gm)].map(m => m[0]);
vm.runInContext(dAssign.join('\n'), ctx);
const TRm = vm.runInContext('TR', ctx), CTL = vm.runInContext('CTL', ctx), PROF = vm.runInContext('PROF', ctx);
const GM = vm.runInContext('GM', ctx), GI = vm.runInContext('GI', ctx), RT = vm.runInContext('RT', ctx), HWS = vm.runInContext('HWS', ctx), D = vm.runInContext('D', ctx);

const tr = (s, l) => { if (s == null) return s; if (l === 'ru') return s; const t = TRm[s]; return t ? (l === 'ua' ? t[0] : t[1]) : s; };
const res = { ru: {}, uk: {}, en: {} };
const put = (k, ru) => { res.ru[k] = ru; res.uk[k] = tr(ru, 'ua'); res.en[k] = tr(ru, 'en'); };
const missing = [];

// groups
const grpKeys = {}; let gi = 0;
const gslug = g => grpKeys[g] || (grpKeys[g] = 'grp.' + (++gi));
// tweaks
const defs = [];
for (const c of CTL) {
  for (const f of ['name', 'sub', 'desc', 'stab', 'lat', 'temp', 'note', 'unit']) if (c[f] != null) {
    put(`tw.${c.id}.${f}`, c[f]);
    if (f !== 'unit' && TRm[c[f]] == null) missing.push(c.id + '.' + f);
  }
  if (c.labels) c.labels.forEach((l, i) => put(`tw.${c.id}.lbl${i}`, l));
  if (c.grp) put(gslug(c.grp), c.grp);
  defs.push(c);
}
// profiles
PROF.forEach(p => { put(`prof.${p.id}.name`, p.name); put(`prof.${p.id}.sub`, p.sub); });
// generic words
const words = { 'risk.0': 'Безопасно', 'risk.1': 'Умеренно', 'risk.2': 'Высокий риск', 'risk.std': 'Стандарт', 'risk.guard': 'Защита',
  'tot.0': 'Низкий', 'tot.1': 'Умеренный', 'tot.2': 'Повышенный', 'tot.3': 'Высокий', 'tip.nofps': 'Без влияния', 'tip.atmax': 'на максимуме',
  'tip.stab': 'Стабильность', 'tip.lat': 'Отклик', 'tip.temp': 'Температура', 'tip.sliderwarn': 'Зелёная часть шкалы безопасна для большинства систем. Красная требует хорошего охлаждения и может вызывать сбои.',
  'tip.foot': 'Оценка для типичной игровой системы. Значения ориентировочные (~).', 'tip.more': 'Подробнее: ', 'ov.intro': 'Состояние системы, готовые профили и защита. Профиль задаёт стартовые значения, дальше всё можно подправить вручную в разделах.',
  'ov.cpu': 'Температура CPU', 'ov.gpu': 'Температура GPU', 'ov.ram': 'Оперативная память', 'ov.load': 'Загрузка ', 'ov.of32': 'из 32 ГБ', 'ov.demo': 'демо',
  'ov.safe': 'безопасно', 'ov.limit': 'предел', 'ov.risk': 'Общий уровень риска', 'ov.gaincap': 'Ожидаемый прирост FPS от активных настроек. Эффекты частично перекрываются, поэтому верхняя граница не превышает 20%.',
  'toast.prof1': 'Профиль «', 'toast.prof2': '» выбран. Нажмите «Применить».', 'toast.discard': 'Изменения отменены', 'toast.applied': 'Применено: ', 'toast.rp': '. Точка восстановления создана (демо).', 'on': 'включено', 'sections': 'Разделы', 'offon': 'выкл → вкл', 'gb': 'ГБ' };
for (const [k, v] of Object.entries(words)) { put(k, v); if (TRm[v] == null && !/^(on|gb)$/.test(k)) missing.push('word:' + k); }
// UI strings D
for (const k of Object.keys(D.ru)) { res.ru[k] = D.ru[k]; res.uk[k] = D.ua[k] ?? D.ru[k]; res.en[k] = D.en[k] ?? D.ru[k]; }
// D keys that clash with word keys (ru strings only): keep D versions
const dup = Object.keys(D.ru).filter(k => k in words);
// game text keys
const games = GM.map(g => ({ id: g.n, regex: g.r.source, heavy: !!g.t, ig: { ru: g.ig, uk: GI[g.n][0], en: GI[g.n][1] } }));

// ---- write resx
const esc = s => String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;');
const header = `<?xml version="1.0" encoding="utf-8"?>\n<root>\n  <resheader name="resmimetype"><value>text/microsoft-resx</value></resheader>\n  <resheader name="version"><value>2.0</value></resheader>\n  <resheader name="reader"><value>System.Resources.ResXResourceReader, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>\n  <resheader name="writer"><value>System.Resources.ResXResourceWriter, System.Windows.Forms, Version=4.0.0.0, Culture=neutral, PublicKeyToken=b77a5c561934e089</value></resheader>\n`;
const resDir = path.join(out, 'src/Corvite.App/Resources'); fs.mkdirSync(resDir, { recursive: true });
const files = { ru: 'Strings.resx', uk: 'Strings.uk.resx', en: 'Strings.en.resx' };
for (const l of ['ru', 'uk', 'en'])
  fs.writeFileSync(path.join(resDir, files[l]), header + Object.keys(res.ru).sort().map(k => `  <data name="${esc(k)}" xml:space="preserve"><value>${esc(res[l][k])}</value></data>`).join('\n') + '\n</root>\n');

// ---- catalog (C#)
const q = s => JSON.stringify(s);
const cs = [];
cs.push('// <auto-generated> from prototype Corvite v2.1.html by tools/gen.js. Do not edit by hand. </auto-generated>');
cs.push('namespace Corvite.Core.Catalog;\n\npublic static partial class TweakCatalog\n{\n    public static readonly IReadOnlyList<TweakDef> All = new TweakDef[]\n    {');
for (const c of defs) {
  const vend = c.v ? `Vendor.${({ amd: 'AmdCpu', intel: 'IntelCpu', nv: 'NvidiaGpu', rx: 'AmdGpu' })[c.v]}` : 'Vendor.Any';
  const sec = ({ overview: 'Section.Overview', system: 'Section.Windows', cpu: 'Section.Cpu', gpu: 'Section.Gpu', memory: 'Section.Memory', network: 'Section.Network', background: 'Section.Background' })[c.sec];
  const base = `Id: ${q(c.id)}, Section: ${sec}, GroupKey: ${q(gslug(c.grp || ''))}, Vendor: ${vend}, IsGuard: ${!!c.guard ? 'true' : 'false'}, FpsMin: ${c.g[0]}, FpsMax: ${c.g[1]}, HasNote: ${c.note ? 'true' : 'false'}`;
  if (c.type === 'toggle') cs.push(`        new TweakDef(${base}, Kind: TweakKind.Toggle, Risk: ${c.risk}, DefaultOn: ${c.def ? 'true' : 'false'}),`);
  else cs.push(`        new TweakDef(${base}, Kind: TweakKind.Slider, Min: ${c.min}, Max: ${c.max}, Step: ${c.step}, UnitKey: ${c.unit ? q('tw.' + c.id + '.unit') : 'null'}, ShowPlus: ${c.plus ? 'true' : 'false'}, LabelCount: ${c.labels ? c.labels.length : 0}, DefaultValue: ${c.def ?? c.min}),`);
}
cs.push('    };\n}');
const cdir = path.join(out, 'src/Corvite.Core/Catalog'); fs.mkdirSync(cdir, { recursive: true });
fs.writeFileSync(path.join(cdir, 'TweakCatalog.g.cs'), cs.join('\n') + '\n');

// ---- data json
const ddir = path.join(out, 'src/Corvite.Core/Data'); fs.mkdirSync(ddir, { recursive: true });
fs.writeFileSync(path.join(ddir, 'games.json'), JSON.stringify(games, null, 1));
const ramOut = {}; for (const ty of Object.keys(RT)) { ramOut[ty] = {}; for (const v of Object.keys(RT[ty])) ramOut[ty][v] = RT[ty][v].map(r => ({ speed: r[0], timings: r[1], dram: r[2], ctrl: r[3] })); }
fs.writeFileSync(path.join(ddir, 'ram_timings.json'), JSON.stringify(ramOut, null, 1));
fs.writeFileSync(path.join(ddir, 'demo_hardware.json'), JSON.stringify(HWS, null, 1));
console.log(JSON.stringify({ tweaks: defs.length, keys: Object.keys(res.ru).length, games: games.length, groups: gi, missingTranslations: missing, dupKeys: dup }));
