// Writes src/MatPhone/wwwroot/ui/mp-icons.svg: the icons of the app that the toolkit's sprite does not have (the telephony ones), taken from the
// Lucide library that MatDesign vendors (tools/iconlib/lucide.json, ISC licence), plus the glyph of the logo.
//   node tools/app-icons.js [path of the MatDesign repository, default ../MatDesign]
const fs = require('fs');
const path = require('path');

const design = process.argv[2] || path.join(__dirname, '..', '..', 'MatDesign');
const lucide = JSON.parse(fs.readFileSync(path.join(design, 'tools', 'iconlib', 'lucide.json'), 'utf8')).icons;

// [id in the sprite, name in Lucide]
const fromLucide = [
  ['phone', 'phone'], ['popout', 'picture-in-picture-2'], ['phone-call', 'phone-call'], ['phone-incoming', 'phone-incoming'], ['phone-outgoing', 'phone-outgoing'],
  ['phone-missed', 'phone-missed'], ['phone-off', 'phone-off'], ['phone-forwarded', 'phone-forwarded'], ['voicemail', 'voicemail'],
  ['mic', 'mic'], ['mic-off', 'mic-off'], ['dialpad', 'grid-3x3'], ['transfer', 'arrow-right-left'], ['bell-off', 'bell-off'],
  ['headset', 'headset'], ['hash', 'hash'], ['keyboard', 'keyboard'], ['scroll-text', 'scroll-text'], ['split', 'split'],
  ['radio-tower', 'radio-tower'], ['contact', 'contact'], ['list-ordered', 'list-ordered'], ['power', 'power'], ['circle-dot', 'circle-dot'],
  ['backspace', 'delete'], ['router', 'router'], ['signal', 'signal'], ['user-plus', 'user-plus'], ['lock-open', 'lock-open'],
  ['send', 'send'], ['undo', 'undo-2'], ['wifi-off', 'wifi-off'], ['building', 'building'], ['ban', 'ban'], ['repeat', 'repeat'],
  ['grip', 'grip-vertical'], ['trending-up', 'trending-up'], ['share', 'share-2'],
];

// The handset of the logo, filled: the outline of Lucide's phone closed into a solid shape.
const handset = "M13.832 16.568a1 1 0 0 0 1.213-.303l.355-.465A2 2 0 0 1 17 15h3a2 2 0 0 1 2 2v3a2 2 0 0 1-2 2A18 18 0 0 1 2 4a2 2 0 0 1 2-2h3a2 2 0 0 1 2 2v3a2 2 0 0 1-.8 1.6l-.468.351a1 1 0 0 0-.292 1.233 14 14 0 0 0 6.392 6.384z";

const lines = [
  '<svg xmlns="http://www.w3.org/2000/svg" class="mat-sprite" width="0" height="0" aria-hidden="true" focusable="false">',
  '  <!-- Written by tools/app-icons.js (do not edit): the icons of MatPhone that the sprite of MatUi has not, from Lucide (ISC), and the glyph of the logo. -->',
  `  <symbol id="mat-i-mp-logo" viewBox="0 0 24 24" fill="currentColor" stroke="currentColor" stroke-width="1.2" stroke-linejoin="round"><path d="${handset}"/></symbol>`,
];
for (const [id, name] of fromLucide) {
  const icon = lucide[name];
  if (!icon) {
    throw new Error(`Lucide has no icon "${name}"`);
  }

  lines.push(`  <symbol id="mat-i-${id}" viewBox="0 0 24 24">${icon.body}</symbol>`);
}

lines.push('</svg>', '');
const target = path.join(__dirname, '..', 'src', 'MatPhone', 'wwwroot', 'ui', 'mp-icons.svg');
fs.writeFileSync(target, lines.join('\n'));
console.log(`wrote ${fromLucide.length + 1} icons to ${target}`);
