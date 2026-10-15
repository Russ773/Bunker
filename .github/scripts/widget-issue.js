// Helpers for the widget submission workflows. Everything the issue contains is untrusted text: it is read from the environment,
// written to files and judged by the app's own widget rules (Bunker.exe --check-widget). Nothing from an issue is ever run.
//
//   node widget-issue.js extract <widget.json>                         read the widget out of the issue body (ISSUE_BODY)
//   node widget-issue.js comment <result.json> <comment.md> <labels.json>   write the reply and the labels to set
//   node widget-issue.js add <widget.json> <result.json> <comment.md>        put the widget in widgets/ and widgets/index.json

const fs = require('fs');
const path = require('path');

const [command, ...args] = process.argv.slice(2);

/** The text of the "Widget file" box of the issue form: a fenced block, or failing that everything from the first { to the last }. */
function extractJson(body) {
  const section = /###\s*Widget file\s*\n([\s\S]*?)(?:\n###\s|$)/i.exec(body || '');
  const text = section ? section[1] : body || '';
  const fenced = /```(?:json)?\s*\n([\s\S]*?)\n```/i.exec(text);
  if (fenced) return fenced[1].trim();
  const first = text.indexOf('{');
  const last = text.lastIndexOf('}');
  return first >= 0 && last > first ? text.slice(first, last + 1).trim() : '';
}

function libraryVersion(id) {
  const file = path.join('widgets', `${id}.json`);
  if (!fs.existsSync(file)) return null;
  try { return JSON.parse(fs.readFileSync(file, 'utf8')).version || '0.0.0'; } catch { return '0.0.0'; }
}

function newer(a, b) {
  const x = String(a).split('.').map(Number);
  const y = String(b).split('.').map(Number);
  for (let i = 0; i < Math.max(x.length, y.length); i++) {
    if ((x[i] || 0) !== (y[i] || 0)) return (x[i] || 0) > (y[i] || 0);
  }
  return false;
}

/** Problems that only the library can see: an id that is already taken, or an update that is not a higher version. */
function libraryProblems(result) {
  const problems = [];
  if (result.ok) {
    const have = libraryVersion(result.id);
    if (have !== null && !newer(result.version, have)) {
      problems.push(`The library already has "${result.id}" at version ${have}. To update it, raise "version" above ${have}. To add a different widget, give it its own "id".`);
    }
  }
  return problems;
}

function escapeCell(text) {
  return String(text).replace(/\|/g, '\\|').replace(/\r?\n/g, ' ');
}

if (command === 'extract') {
  const json = extractJson(process.env.ISSUE_BODY);
  fs.writeFileSync(args[0], json);
  console.log(json ? `Read ${json.length} characters of widget JSON.` : 'No widget JSON found in the issue.');
} else if (command === 'comment') {
  const [resultFile, commentFile, labelsFile] = args;
  const result = JSON.parse(fs.readFileSync(resultFile, 'utf8'));
  const errors = [...(result.errors || []), ...libraryProblems(result)];
  const ok = result.ok && errors.length === 0;
  const lines = [];
  if (ok) {
    const risky = result.commands.filter((c) => c.risk !== 'Normal');
    lines.push('### ✅ This widget passes the automatic checks', '');
    lines.push('| | |', '| --- | --- |');
    lines.push(`| Name | ${escapeCell(result.name)} |`, `| Id | \`${result.id}\` |`, `| Author | ${escapeCell(result.author)} |`, `| Version | ${result.version} |`);
    lines.push('', `**Every command it can send** (${result.commands.length}):`, '');
    for (const c of result.commands) lines.push(`- ${c.risk === 'Normal' ? '' : '⚠️ '}\`${c.command.replace(/`/g, "'")}\``);
    if (risky.length) lines.push('', `${risky.length} command${risky.length === 1 ? ' is' : 's are'} marked ⚠️ because they can affect players or the server. The reviewer will look at those closely.`);
    lines.push('', 'A person will now read it. If it is accepted it is added to **Browse library** and everyone can install it.');
  } else {
    lines.push('### ❌ This widget can\'t be added yet', '', ...errors.map((e) => `- ${e}`));
    lines.push('', 'Edit the widget file in the issue above to fix this. It is checked again as soon as you save.');
  }
  fs.writeFileSync(commentFile, lines.join('\n') + '\n');
  fs.writeFileSync(labelsFile, JSON.stringify({ add: ok ? 'widget-valid' : 'widget-needs-changes', remove: ok ? 'widget-needs-changes' : 'widget-valid' }));
  console.log(ok ? 'Widget is valid.' : 'Widget has problems: ' + errors.join(' | '));
} else if (command === 'add') {
  const [widgetFile, resultFile, commentFile] = args;
  const result = JSON.parse(fs.readFileSync(resultFile, 'utf8'));
  const problems = [...(result.errors || []), ...libraryProblems(result)];
  if (!result.ok || problems.length > 0) {
    fs.writeFileSync(commentFile, `### ❌ Could not add this widget\n\n${problems.map((p) => `- ${p}`).join('\n')}\n`);
    process.exitCode = 1;
  } else {
    // The id was checked to be lower-case letters, numbers, - and _, so it is safe as a file name.
    const widgetPath = path.join('widgets', `${result.id}.json`);
    const widget = JSON.parse(fs.readFileSync(widgetFile, 'utf8'));
    fs.writeFileSync(widgetPath, JSON.stringify(widget, null, 2) + '\n');
    const indexPath = path.join('widgets', 'index.json');
    const index = JSON.parse(fs.readFileSync(indexPath, 'utf8'));
    const name = `${result.id}.json`;
    if (!index.widgets.includes(name)) index.widgets.push(name);
    index.widgets.sort();
    fs.writeFileSync(indexPath, JSON.stringify(index, null, 2) + '\n');
    fs.writeFileSync(commentFile, `### 🎉 Added to the library\n\n**${escapeCell(result.name)}** ${result.version} by ${escapeCell(result.author)} is now in **Browse library**. Thank you for sharing it!\n`);
    console.log(`Added ${widgetPath}`);
  }
} else {
  console.error('Unknown command: ' + command);
  process.exit(2);
}
