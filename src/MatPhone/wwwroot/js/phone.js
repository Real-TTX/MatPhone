/* MatPhone web client: the phone of a person in the browser (Pages/Phone.cshtml).
   Signalling over the WebSocket /ws/phone (JSON, one message per frame); audio over WebRTC, G.711 inside DTLS-SRTP. The server always offers, the browser answers.
   - incoming {leg, number, name, sdp}: it rings here until the person accepts (answer with our SDP) or rejects.
   - dial: the microphone first, then {t:"dial"}; the server answers with outgoing {leg, sdp}, we answer at once and play the ring tone until "connected".
   - state {leg, state: ringing | connected | held | remote-held}, ended {leg, reason}; presence {id, state}; me {dnd, forward, voicemail, groups}.
   Every text comes from data-text-* of the page (the language of the person). The ring tones are made here (WebAudio): no audio files, nothing from elsewhere. */
(function () {
  'use strict';

  var root = document.querySelector('[data-phone]');
  var dataEl = document.getElementById('mp-phone-data');
  if (!root || !dataEl) { return; }

  var data;
  try { data = JSON.parse(dataEl.textContent || '{}'); } catch (e) { return; }

  var $ = function (sel, scope) { return (scope || document).querySelector(sel); };
  var $$ = function (sel, scope) { return Array.prototype.slice.call((scope || document).querySelectorAll(sel)); };
  var text = function (name) { return root.getAttribute('data-text-' + name) || ''; };
  var toast = function (message, tone) { if (window.MatUi && window.MatUi.toast) { window.MatUi.toast(message, { tone: tone || 'info' }); } };
  var calls = {};            // leg -> call
  var presence = {};          // user id -> state
  var socket = null;
  var retry = 1000;
  var pickupUntil = 0;        // a pickup was asked for: the next incoming call is accepted at once
  var ringHere = true;

  // ================================================================================================ the people

  var people = (data.colleagues || []).slice();
  people.forEach(function (p) { presence[p.id] = p.state || 'offline'; });
  var byId = {};
  people.forEach(function (p) { byId[p.id] = p; });

  function initials(name) {
    var parts = String(name || '').trim().split(/\s+/).filter(Boolean);
    return ((parts[0] || '?').charAt(0) + (parts.length > 1 ? parts[parts.length - 1].charAt(0) : '')).toUpperCase();
  }

  function stateTone(state) {
    return { idle: 'ok', ringing: 'warn', busy: 'error', dnd: 'busy' }[state] || 'plain';
  }

  function stateText(state) { return text('presence-' + (state || 'offline')); }

  function el(tag, cls, content) {
    var node = document.createElement(tag);
    if (cls) { node.className = cls; }
    if (content !== undefined && content !== null) { node.textContent = content; }
    return node;
  }

  function icon(name) {
    var svg = document.createElementNS('http://www.w3.org/2000/svg', 'svg');
    svg.setAttribute('class', 'mat-icon');
    svg.setAttribute('aria-hidden', 'true');
    svg.setAttribute('focusable', 'false');
    var use = document.createElementNS('http://www.w3.org/2000/svg', 'use');
    use.setAttribute('href', '#mat-i-' + name);
    svg.appendChild(use);
    return svg;
  }

  function renderPeople() {
    var list = $('[data-people]');
    if (!list) { return; }
    var filter = ($('[data-people-filter]').value || '').toLowerCase();
    var team = $('[data-people-team]').value;
    list.textContent = '';
    var shown = people.filter(function (p) {
      return (!filter || p.name.toLowerCase().indexOf(filter) >= 0 || String(p.extension || '').indexOf(filter) >= 0)
        && (!team || (p.teams || []).indexOf(+team) >= 0);
    });
    shown.forEach(function (p) {
      var state = presence[p.id] || 'offline';
      var li = el('li', 'mat-item');
      var main = el('button', 'mat-item__main');
      main.type = 'button';
      main.setAttribute('data-person', p.id);
      var lead = el('span', 'mat-item__lead');
      var avatar = el('span', 'mat-avatar', initials(p.name));
      avatar.setAttribute('aria-hidden', 'true');
      lead.appendChild(avatar);
      var body = el('span', 'mat-item__body');
      body.appendChild(el('span', 'mat-item__title', p.name));
      body.appendChild(el('span', 'mat-item__text', p.extension ? text('ext') + ' ' + p.extension : ''));
      var end = el('span', 'mat-item__end');
      end.appendChild(el('span', 'mat-pill mat-pill--' + stateTone(state), stateText(state)));
      main.appendChild(lead);
      main.appendChild(body);
      main.appendChild(end);
      main.setAttribute('aria-label', (state === 'ringing' ? text('pickup') : text('call')) + ': ' + p.name + ', ' + stateText(state));
      li.appendChild(main);
      list.appendChild(li);
    });
    $('[data-people-empty]').hidden = shown.length > 0;
  }

  function renderTeams() {
    var select = $('[data-people-team]');
    (data.teams || []).forEach(function (t) {
      var option = el('option', null, t.name);
      option.value = t.id;
      select.appendChild(option);
    });
  }

  // ================================================================================================ the function keys

  function renderKeys() {
    var box = $('[data-keys]');
    var keys = data.keys || [];
    $('[data-keys-card]').hidden = keys.length === 0;
    box.textContent = '';
    keys.forEach(function (k, i) {
      var b = el('button', 'mp-key');
      b.type = 'button';
      b.setAttribute('data-key-index', i);
      var lamp = el('span', 'mp-key__lamp');
      lamp.setAttribute('aria-hidden', 'true');
      var state = keyState(k);
      lamp.setAttribute('data-state', state.lamp);
      b.appendChild(lamp);
      var label = el('span', 'mp-key__label', k.label);
      b.appendChild(label);
      if (state.text) {
        b.appendChild(el('span', 'mp-key__state', state.text));
      }

      b.setAttribute('aria-label', k.label + (state.text ? ', ' + state.text : ''));
      if (state.pressed !== undefined) { b.setAttribute('aria-pressed', state.pressed ? 'true' : 'false'); }
      box.appendChild(b);
    });
  }

  function keyState(k) {
    switch (k.kind) {
      case 'BusyLamp':
        var s = presence[k.user] || 'offline';
        return { lamp: s, text: stateText(s) };
      case 'GroupToggle':
        var g = (data.groups || []).filter(function (x) { return x.id === k.group; })[0];
        return { lamp: g && g.on ? 'on' : 'off', text: g ? (g.on ? text('group-on') : text('group-off')) : '', pressed: !!(g && g.on) };
      case 'DoNotDisturb':
        return { lamp: data.me.dnd ? 'dnd' : 'off', pressed: !!data.me.dnd };
      case 'Forward':
        var on = !!data.me.forward && data.me.forward === k.number;
        return { lamp: on ? 'on' : 'off', pressed: on };
      case 'Mailbox':
        return { lamp: data.me.voicemail > 0 ? 'on' : 'off', text: data.me.voicemail > 0 ? String(data.me.voicemail) : '' };
      default:
        return { lamp: 'none' };
    }
  }

  function pressKey(k) {
    switch (k.kind) {
      case 'BusyLamp':
        var person = byId[k.user];
        if (presence[k.user] === 'ringing') { pickup(k.user); } else if (person && person.extension) { dial(person.extension); }
        break;
      case 'SpeedDial':
        dial(k.number);
        break;
      case 'GroupToggle':
        send({ t: 'group', group: k.group });
        break;
      case 'DoNotDisturb':
        send({ t: 'dnd' });
        break;
      case 'Forward':
        send({ t: 'forward', number: k.number });
        break;
      case 'Mailbox':
        location.href = '/Voicemail';
        break;
    }
  }

  // ================================================================================================ my state

  function renderMe() {
    var me = data.me;
    var line = $('[data-phone-me]');
    line.textContent = me.name + (me.extension ? ' · ' + text('ext') + ' ' + me.extension : '');
    $('[data-me-dnd]').checked = !!me.dnd;
    var fw = $('[data-me-forward]');
    fw.hidden = !me.forward;
    fw.textContent = me.forward ? (fw.getAttribute('data-text') || '').replace('{0}', me.forward) : '';
    var vm = $('[data-me-voicemail]');
    vm.hidden = !(me.voicemail > 0);
    vm.textContent = me.voicemail > 0 ? String(me.voicemail) : '';
    var groups = $('[data-me-groups]');
    groups.textContent = '';
    (data.groups || []).forEach(function (g) {
      var li = el('li', 'mat-item');
      var label = el('label', 'mat-item__main');
      var body = el('span', 'mat-item__body');
      body.appendChild(el('span', 'mat-item__title', g.name));
      body.appendChild(el('span', 'mat-item__text', g.number ? text('ext') + ' ' + g.number : ''));
      var sw = el('span', 'mat-item__end');
      var switchLabel = el('span', 'mat-switch');
      var input = el('input');
      input.type = 'checkbox';
      input.checked = !!g.on;
      input.setAttribute('data-group-toggle', g.id);
      input.setAttribute('aria-label', g.name);
      switchLabel.appendChild(input);
      switchLabel.appendChild(el('span', 'mat-switch__track'));
      sw.appendChild(switchLabel);
      label.appendChild(body);
      label.appendChild(sw);
      li.appendChild(label);
      groups.appendChild(li);
    });
    document.title = (me.voicemail > 0 ? '(' + me.voicemail + ') ' : '') + document.title.replace(/^\(\d+\)\s/, '');
  }

  // ================================================================================================ recent calls

  function renderRecent() {
    var list = $('[data-recent]');
    var recent = data.recent || [];
    list.textContent = '';
    recent.forEach(function (c) {
      if (!c.number) { return; }
      var li = el('li', 'mat-item');
      var main = el('button', 'mat-item__main');
      main.type = 'button';
      main.setAttribute('data-redial', c.number);
      var lead = el('span', 'mat-item__lead');
      var missed = c.result === 'Missed' || c.result === 'Busy' || c.result === 'Rejected' || c.result === 'Failed' || c.result === 'Cancelled';
      lead.appendChild(icon(missed ? 'phone-missed' : (c.incoming ? 'phone-incoming' : 'phone-outgoing')));
      var body = el('span', 'mat-item__body');
      body.appendChild(el('span', 'mat-item__title', c.name || c.shown));
      var when = new Date(c.when);
      body.appendChild(el('span', 'mat-item__text', (c.name ? c.shown + ' · ' : '') + when.toLocaleString(document.documentElement.getAttribute('data-locale') || undefined, { dateStyle: 'short', timeStyle: 'short' }) + ' · ' + c.label));
      main.appendChild(lead);
      main.appendChild(body);
      main.setAttribute('aria-label', text('call') + ': ' + (c.name || c.shown));
      li.appendChild(main);
      list.appendChild(li);
    });
    $('[data-recent-empty]').hidden = list.children.length > 0;
  }

  // ================================================================================================ tones

  var audioContext = null;
  function audio() {
    if (!audioContext) {
      var Ctx = window.AudioContext || window.webkitAudioContext;
      if (!Ctx) { return null; }
      audioContext = new Ctx();
    }

    if (audioContext.state === 'suspended') { audioContext.resume(); }
    return audioContext;
  }

  // A tone pattern: [[frequency or two, milliseconds on], milliseconds off ...], repeated until stopped.
  function tone(pattern, volume) {
    var ctx = audio();
    if (!ctx) { return { stop: function () { } }; }
    var stopped = false;
    var timer = null;
    var nodes = [];
    function play(i) {
      if (stopped) { return; }
      var step = pattern[i % pattern.length];
      if (Array.isArray(step)) {
        var gain = ctx.createGain();
        gain.gain.value = volume || 0.08;
        gain.connect(ctx.destination);
        (Array.isArray(step[0]) ? step[0] : [step[0]]).forEach(function (f) {
          var osc = ctx.createOscillator();
          osc.frequency.value = f;
          osc.connect(gain);
          osc.start();
          osc.stop(ctx.currentTime + step[1] / 1000);
          nodes.push(osc);
        });
        timer = setTimeout(function () { play(i + 1); }, step[1]);
      } else {
        timer = setTimeout(function () { play(i + 1); }, step);
      }
    }
    play(0);
    return { stop: function () { stopped = true; clearTimeout(timer); nodes.forEach(function (n) { try { n.stop(); } catch (e) { } }); } };
  }

  // The German ring back tone (425 Hz, 1 s on, 4 s off), the ringer (a two-tone chime) and the busy tone.
  var ringback = function () { return tone([[425, 1000], 4000], 0.06); };
  var ringer = function () { return tone([[[880, 1320], 300], 120, [[660, 990], 300], 1800], 0.12); };
  function busyTone() { var t = tone([[425, 480], 480], 0.06); setTimeout(t.stop, 2900); }
  function dtmfTone(key) {
    var map = { '1': [697, 1209], '2': [697, 1336], '3': [697, 1477], '4': [770, 1209], '5': [770, 1336], '6': [770, 1477], '7': [852, 1209], '8': [852, 1336], '9': [852, 1477], '*': [941, 1209], '0': [941, 1336], '#': [941, 1477] };
    if (map[key]) { var t = tone([[map[key], 120], 10000], 0.05); setTimeout(t.stop, 130); }
  }

  // ================================================================================================ the WebSocket

  function setConnection(state) {
    var pill = $('[data-phone-connection]');
    pill.textContent = pill.getAttribute('data-text-' + state) || state;
    pill.className = 'mat-pill mat-pill--' + ({ online: 'ok', connecting: 'warn', offline: 'error' }[state] || 'plain');
  }

  function connect() {
    setConnection('connecting');
    var url = (location.protocol === 'https:' ? 'wss://' : 'ws://') + location.host + '/ws/phone';
    socket = new WebSocket(url);
    socket.onopen = function () {
      retry = 1000;
      setConnection('online');
      send({ t: 'hello', ring: ringHere });
    };
    socket.onmessage = function (event) {
      var message;
      try { message = JSON.parse(event.data); } catch (e) { return; }
      handle(message);
    };
    socket.onclose = function () {
      setConnection('offline');
      Object.keys(calls).forEach(function (leg) { endCall(leg, null); });
      setTimeout(connect, retry);
      retry = Math.min(retry * 2, 30000);
    };
  }

  function send(message) {
    if (socket && socket.readyState === WebSocket.OPEN) { socket.send(JSON.stringify(message)); }
  }

  setInterval(function () { send({ t: 'ping' }); }, 25000);

  function handle(m) {
    switch (m.t) {
      case 'welcome':
        Object.keys(m.presence || {}).forEach(function (id) { presence[id] = m.presence[id]; });
        renderPeople();
        renderKeys();
        break;
      case 'presence':
        presence[m.id] = m.state;
        renderPeople();
        renderKeys();
        break;
      case 'me':
        data.me.dnd = m.dnd;
        data.me.forward = m.forward;
        data.me.voicemail = m.voicemail;
        data.groups = m.groups || [];
        renderMe();
        renderKeys();
        break;
      case 'incoming':
        incoming(m);
        break;
      case 'outgoing':
        outgoing(m);
        break;
      case 'state':
        state(m.leg, m.state);
        break;
      case 'ended':
        endCall(m.leg, m.reason);
        break;
      case 'error':
        toast(text({ transfer: 'transfer-failed', pickup: 'pickup-failed', 'not-allowed': 'not-allowed' }[m.message] || 'failed'), 'danger');
        break;
    }
  }

  // ================================================================================================ calls

  var localStream = null;
  function microphone() {
    if (localStream && localStream.getAudioTracks().some(function (t) { return t.readyState === 'live'; })) { return Promise.resolve(localStream); }
    return navigator.mediaDevices.getUserMedia({ audio: { echoCancellation: true, noiseSuppression: true, autoGainControl: true }, video: false }).then(function (stream) {
      localStream = stream;
      return stream;
    });
  }

  function releaseMicrophone() {
    if (Object.keys(calls).length === 0 && localStream) {
      localStream.getTracks().forEach(function (t) { t.stop(); });
      localStream = null;
    }
  }

  function createCard(call) {
    var card = $('[data-call-template]').content.firstElementChild.cloneNode(true);
    card.setAttribute('data-leg', call.leg);
    $('[data-call-avatar]', card).textContent = initials(call.name || call.number);
    $('[data-call-name]', card).textContent = call.name || call.number || text('unknown');
    $('[data-call-number]', card).textContent = call.name ? call.number || '' : '';
    $('[data-calls]').prepend(card);
    call.card = card;
    return card;
  }

  function setCardState(call, key, tone) {
    var pill = $('[data-call-state]', call.card);
    pill.textContent = text(key);
    pill.className = 'mat-pill mat-pill--' + (tone || 'plain');
    var active = key === 'connected' || key === 'held' || key === 'remote-held';
    $('[data-call-ringing]', call.card).hidden = !(call.incoming && !call.answered);
    $('[data-call-active]', call.card).hidden = !(call.answered || !call.incoming);
    $('[data-call-mute]', call.card).disabled = !active;
    $('[data-call-hold]', call.card).disabled = !active;
    $('[data-call-keypad]', call.card).disabled = !active;
    $('[data-call-transfer]', call.card).disabled = !active;
  }

  function peer(call) {
    var pc = new RTCPeerConnection({ iceServers: [] });
    pc.ontrack = function (event) {
      var player = $('[data-call-audio]', call.card);
      player.srcObject = event.streams[0] || new MediaStream([event.track]);
      player.play().catch(function () { });
    };
    pc.onicecandidate = function (event) {
      if (event.candidate) { send({ t: 'candidate', leg: call.leg, candidate: event.candidate.toJSON() }); }
    };
    call.pc = pc;
    return pc;
  }

  function answerOffer(call, sdp) {
    var pc = peer(call);
    return microphone().then(function (stream) {
      stream.getAudioTracks().forEach(function (track) { pc.addTrack(track, stream); });
      return pc.setRemoteDescription({ type: 'offer', sdp: sdp });
    }).then(function () { return pc.createAnswer(); })
      .then(function (answer) { return pc.setLocalDescription(answer); })
      .then(function () { return gathered(pc); })
      .then(function () { return pc.localDescription.sdp; });
  }

  // Waits until the browser has its candidates (at most a second: the host candidates are there at once, the server finds the rest by itself).
  function gathered(pc) {
    if (pc.iceGatheringState === 'complete') { return Promise.resolve(); }
    return new Promise(function (resolve) {
      var done = function () { if (pc.iceGatheringState === 'complete') { resolve(); } };
      pc.addEventListener('icegatheringstatechange', done);
      setTimeout(resolve, 1000);
    });
  }

  function incoming(m) {
    var call = { leg: m.leg, incoming: true, number: m.number, name: m.name, offer: m.sdp, answered: false };
    calls[m.leg] = call;
    createCard(call);
    setCardState(call, 'incoming', 'warn');
    if (Date.now() < pickupUntil) {
      pickupUntil = 0;
      accept(call);
      return;
    }

    call.tone = ringer();
    notify(call);
  }

  function accept(call) {
    if (call.answered) { return; }
    call.answered = true;
    stopTone(call);
    setCardState(call, 'connected', 'ok');
    answerOffer(call, call.offer).then(function (sdp) {
      send({ t: 'answer', leg: call.leg, sdp: sdp });
    }).catch(function () {
      toast(text('mic-denied'), 'danger');
      send({ t: 'reject', leg: call.leg });
      endCall(call.leg, null);
    });
  }

  function outgoing(m) {
    var call = { leg: m.leg, incoming: false, number: m.number, name: nameOf(m.number), answered: false };
    calls[m.leg] = call;
    createCard(call);
    setCardState(call, 'outgoing', 'info');
    answerOffer(call, m.sdp).then(function (sdp) {
      send({ t: 'answer', leg: call.leg, sdp: sdp });
    }).catch(function () {
      toast(text('mic-denied'), 'danger');
      send({ t: 'hangup', leg: call.leg });
      endCall(call.leg, null);
    });
  }

  function nameOf(number) {
    var person = people.filter(function (p) { return p.extension && p.extension === number; })[0];
    return person ? person.name : null;
  }

  function state(leg, value) {
    var call = calls[leg];
    if (!call) { return; }
    if (value === 'ringing') {
      setCardState(call, 'ringing', 'warn');
      if (!call.incoming && !call.tone) { call.tone = ringback(); }
    } else if (value === 'connected') {
      stopTone(call);
      call.answered = true;
      call.connectedAt = call.connectedAt || Date.now();
      setCardState(call, 'connected', 'ok');
      startTimer(call);
    } else if (value === 'held' || value === 'remote-held') {
      setCardState(call, value, 'busy');
    }
  }

  function startTimer(call) {
    if (call.timer) { return; }
    var out = $('[data-call-timer]', call.card);
    call.timer = setInterval(function () {
      var s = Math.floor((Date.now() - call.connectedAt) / 1000);
      out.textContent = Math.floor(s / 60) + ':' + ('0' + (s % 60)).slice(-2);
    }, 1000);
  }

  function stopTone(call) {
    if (call.tone) { call.tone.stop(); call.tone = null; }
  }

  function endCall(leg, reason) {
    var call = calls[leg];
    if (!call) { return; }
    stopTone(call);
    clearInterval(call.timer);
    if (call.pc) { try { call.pc.close(); } catch (e) { } }
    if (call.notification) { call.notification.close(); }
    delete calls[leg];
    var reasons = { '486': 'busy', '480': 'unavailable', '404': 'notfound', '403': 'forbidden', '488': 'failed', '408': 'failed', '503': 'unavailable', '484': 'notfound' };
    if (reason && reasons[reason]) {
      toast(text(reasons[reason]), 'warning');
      if (reason === '486') { busyTone(); }
    }

    if (call.card) {
      setCardState(call, 'ended', 'plain');
      $('[data-call-ringing]', call.card).hidden = true;
      $('[data-call-active]', call.card).hidden = true;
      $('[data-call-pad]', call.card).hidden = true;
      setTimeout(function () { if (call.card.parentNode) { call.card.parentNode.removeChild(call.card); } }, 2500);
    }

    releaseMicrophone();
  }

  function dial(number) {
    number = String(number || '').trim();
    if (!number) { return; }
    // The microphone before the call: the browser asks once, and the audio is ready when the server offers.
    microphone().then(function () {
      send({ t: 'dial', number: number });
    }).catch(function () { toast(text('mic-denied'), 'danger'); });
  }

  function pickup(userId) {
    pickupUntil = Date.now() + 6000;
    microphone().then(function () { send({ t: 'pickup', user: userId }); }).catch(function () { toast(text('mic-denied'), 'danger'); });
  }

  // A notification of the system while the tab is not in front (asked for with the first click on the page).
  function notify(call) {
    if (!document.hidden || !('Notification' in window) || Notification.permission !== 'granted') { return; }
    try {
      call.notification = new Notification(text('call-from').replace('{0}', call.name || call.number || text('unknown')), { tag: 'mp-call-' + call.leg, requireInteraction: true });
      call.notification.onclick = function () { window.focus(); accept(call); };
    } catch (e) { /* not supported here */ }
  }

  // ================================================================================================ the page

  root.addEventListener('click', function (e) {
    var t = e.target;
    var card = t.closest('[data-call]');
    var call = card ? calls[card.getAttribute('data-leg')] : null;
    var key = t.closest('[data-key]');
    if (key) {
      var input = $('[data-dial-input]');
      input.value += key.getAttribute('data-key');
      dtmfTone(key.getAttribute('data-key'));
      input.focus();
      return;
    }

    if (t.closest('[data-dial-backspace]')) {
      var field = $('[data-dial-input]');
      field.value = field.value.slice(0, -1);
      field.focus();
      return;
    }

    var person = t.closest('[data-person]');
    if (person) {
      var id = +person.getAttribute('data-person');
      if (presence[id] === 'ringing') { pickup(id); } else if (byId[id] && byId[id].extension) { dial(byId[id].extension); }
      return;
    }

    var redial = t.closest('[data-redial]');
    if (redial) { dial(redial.getAttribute('data-redial')); return; }

    var fkey = t.closest('[data-key-index]');
    if (fkey) { pressKey((data.keys || [])[+fkey.getAttribute('data-key-index')]); return; }

    if (!call) { return; }
    if (t.closest('[data-call-accept]')) { accept(call); }
    else if (t.closest('[data-call-reject]')) { send({ t: 'reject', leg: call.leg }); endCall(call.leg, null); }
    else if (t.closest('[data-call-hangup]')) { send({ t: 'hangup', leg: call.leg }); endCall(call.leg, null); }
    else if (t.closest('[data-call-mute]')) {
      var mute = t.closest('[data-call-mute]');
      var muted = mute.getAttribute('aria-pressed') !== 'true';
      mute.setAttribute('aria-pressed', muted ? 'true' : 'false');
      if (localStream) { localStream.getAudioTracks().forEach(function (track) { track.enabled = !muted; }); }
    } else if (t.closest('[data-call-hold]')) {
      var hold = t.closest('[data-call-hold]');
      var on = hold.getAttribute('aria-pressed') !== 'true';
      hold.setAttribute('aria-pressed', on ? 'true' : 'false');
      send({ t: 'hold', leg: call.leg, on: on });
    } else if (t.closest('[data-call-keypad]')) {
      var pad = $('[data-call-pad]', card);
      pad.hidden = !pad.hidden;
      t.closest('[data-call-keypad]').setAttribute('aria-pressed', pad.hidden ? 'false' : 'true');
    } else if (t.closest('[data-dtmf]')) {
      var digit = t.closest('[data-dtmf]').getAttribute('data-dtmf');
      dtmfTone(digit);
      send({ t: 'dtmf', leg: call.leg, digit: digit });
    } else if (t.closest('[data-call-transfer]')) {
      var ask = window.MatUi && window.MatUi.prompt
        ? window.MatUi.prompt(text('transfer-prompt'), { title: text('transfer-title') })
        : Promise.resolve(null);
      ask.then(function (number) {
        if (number) { send({ t: 'transfer', leg: call.leg, number: String(number) }); }
      });
    }
  });

  $('[data-dial-form]').addEventListener('submit', function (e) {
    e.preventDefault();
    var input = $('[data-dial-input]');
    dial(input.value);
    input.value = '';
  });

  // Typing on the page (not in a field) dials: digits, * and #, Enter calls, Backspace corrects.
  document.addEventListener('keydown', function (e) {
    var active = document.activeElement;
    if (active && (active.tagName === 'INPUT' || active.tagName === 'SELECT' || active.tagName === 'TEXTAREA' || active.isContentEditable) || e.ctrlKey || e.metaKey || e.altKey) { return; }
    var input = $('[data-dial-input]');
    if (/^[0-9*#+]$/.test(e.key)) {
      var connected = Object.keys(calls).map(function (k) { return calls[k]; }).filter(function (c) { return c.answered; })[0];
      if (connected) { dtmfTone(e.key); send({ t: 'dtmf', leg: connected.leg, digit: e.key }); return; }
      input.value += e.key;
      input.focus();
      e.preventDefault();
    }
  });

  root.addEventListener('input', function (e) {
    if (e.target.matches('[data-people-filter]')) { renderPeople(); }
  });

  root.addEventListener('change', function (e) {
    var t = e.target;
    if (t.matches('[data-people-team]')) { renderPeople(); }
    else if (t.matches('[data-me-dnd]')) { send({ t: 'dnd', on: t.checked }); }
    else if (t.matches('[data-me-ring]')) { ringHere = t.checked; send({ t: 'ring', on: ringHere }); }
    else if (t.matches('[data-group-toggle]')) { send({ t: 'group', group: +t.getAttribute('data-group-toggle'), on: t.checked }); }
  });

  // The first click unlocks the audio of the page (browsers start it only after a gesture) and asks once whether calls may be shown as notifications.
  document.addEventListener('click', function once() {
    audio();
    if ('Notification' in window && Notification.permission === 'default') { Notification.requestPermission().catch(function () { }); }
    document.removeEventListener('click', once);
  });

  // Leaving the page ends the calls of this browser: say so first.
  window.addEventListener('beforeunload', function (e) {
    if (Object.keys(calls).some(function (k) { return calls[k].answered; })) { e.preventDefault(); e.returnValue = ''; }
  });

  renderTeams();
  renderMe();
  renderPeople();
  renderKeys();
  renderRecent();
  connect();
  if (data.dial) { setTimeout(function () { $('[data-dial-input]').value = data.dial; }, 0); }
})();
