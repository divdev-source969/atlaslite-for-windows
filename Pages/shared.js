// Shared by the new-tab and settings pages. Talks to AtlasLite through WebView2 string messages.
(function () {
  'use strict';
  var host = window.chrome && window.chrome.webview;
  var WALLPAPERS = {
    aurora:   ['Aurora',   'linear-gradient(135deg,#0f2027 0%,#203a43 40%,#2c5364 70%,#6a3093 100%)'],
    sunset:   ['Sunset',   'linear-gradient(135deg,#2b1055 0%,#7597de 100%)'],
    ocean:    ['Ocean',    'linear-gradient(160deg,#001d3d 0%,#003566 45%,#0077b6 100%)'],
    forest:   ['Forest',   'linear-gradient(150deg,#0b1f14 0%,#1b4332 50%,#40916c 100%)'],
    midnight: ['Midnight', 'radial-gradient(circle at 20% 10%,#2a2d5a 0%,#12131f 60%,#07070d 100%)'],
    rose:     ['Rose',     'linear-gradient(135deg,#41122c 0%,#a4133c 55%,#ff758f 100%)'],
    ember:    ['Ember',    'linear-gradient(145deg,#1a0a05 0%,#7a2e0e 55%,#f48c06 100%)'],
    mono:     ['Mono',     'linear-gradient(135deg,#0d0d0d 0%,#2b2b2b 100%)'],
    paper:    ['Paper',    'linear-gradient(135deg,#f5f1e8 0%,#dcd6c5 100%)']
  };
  var listeners = [];
  var Atlas = {
    state: null,
    WALLPAPERS: WALLPAPERS,
    send: function (m) { if (host) host.postMessage(String(m)); },
    onState: function (fn) { listeners.push(fn); if (Atlas.state) fn(Atlas.state); },
    wallpaperCss: function (s) {
      if (s.wallpaper === 'custom' && s.customWallpaper) {
        return 'url("' + s.customWallpaper + '") center/cover no-repeat';
      }
      var w = WALLPAPERS[s.wallpaper] || WALLPAPERS.aurora;
      return w[1];
    },
    fgFor: function (hex) {
      var r = parseInt(hex.substr(1, 2), 16), g = parseInt(hex.substr(3, 2), 16), b = parseInt(hex.substr(5, 2), 16);
      return (r * 299 + g * 587 + b * 114) / 1000 > 150 ? '#0b1020' : '#ffffff';
    }
  };
  function apply(s) {
    var root = document.documentElement;
    root.setAttribute('data-theme', s.dark ? 'dark' : 'light');
    if (/^#[0-9a-fA-F]{6}$/.test(s.accent)) {
      root.style.setProperty('--accent', s.accent);
      root.style.setProperty('--accent-fg', Atlas.fgFor(s.accent));
    }
  }
  function receive(text) {
    var s;
    try { s = JSON.parse(text); } catch (e) { return; }
    if (!s || typeof s !== 'object') return;
    Atlas.state = s;
    apply(s);
    for (var i = 0; i < listeners.length; i++) listeners[i](s);
  }
  window.Atlas = Atlas;
  window.__atlasReceive = receive; // used by tests
  if (host) {
    host.addEventListener('message', function (e) { receive(e.data); });
    host.postMessage('get');
  }
})();
