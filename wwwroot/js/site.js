// AeroChat — global JS

// ── Sidebar mobile ──
function openSidebar() {
  document.getElementById('sidebar')?.classList.add('open');
  document.getElementById('sidebarOverlay')?.classList.add('open');
}
function closeSidebar() {
  document.getElementById('sidebar')?.classList.remove('open');
  document.getElementById('sidebarOverlay')?.classList.remove('open');
}

// ── Theme ──
function syncThemePills() {
  var t = localStorage.getItem('ac-theme') || 'dark';
  document.querySelectorAll('.theme-pill-dark').forEach(function(el) {
    el.classList.toggle('active', t !== 'light');
  });
  document.querySelectorAll('.theme-pill-light').forEach(function(el) {
    el.classList.toggle('active', t === 'light');
  });
}
(function() {
  var t = localStorage.getItem('ac-theme') || 'dark';
  document.documentElement.setAttribute('data-theme', t);
  syncThemePills();
})();

// ── Toast ──
var acToastTimer = null;
function showToast(msg, type) {
  var t = document.getElementById('acToast');
  if (!t) return;
  t.textContent = msg;
  t.className = 'toast show' + (type ? ' toast-' + type : '');
  clearTimeout(acToastTimer);
  acToastTimer = setTimeout(function() { t.className = 'toast'; }, 3200);
}

// ── Presence ──
window.acOnlineUsers = {};
function emitPresence() {
  document.dispatchEvent(new CustomEvent('ac:presence'));
}
function markOnline(id) {
  window.acOnlineUsers[id] = 1;
  document.querySelectorAll('[data-userid="' + id + '"]').forEach(function(el) {
    el.classList.add('online');
  });
  emitPresence();
}
function markOffline(id) {
  delete window.acOnlineUsers[id];
  document.querySelectorAll('[data-userid="' + id + '"]').forEach(function(el) {
    el.classList.remove('online');
  });
  emitPresence();
}
function applyPresence() {
  var ids = Object.keys(window.acOnlineUsers);
  document.querySelectorAll('[data-userid]').forEach(function(el) {
    el.classList.toggle('online', ids.indexOf(el.getAttribute('data-userid')) >= 0);
  });
  emitPresence();
}

// ── Sidebar refresh ──
function refreshSidebar() {
  return fetch('/Home/Sidebar', { headers: { 'X-Requested-With': 'XMLHttpRequest' } })
    .then(function(r) { if (!r.ok) throw new Error('sidebar'); return r.text(); })
    .then(function(html) {
      var temp = document.createElement('div');
      temp.innerHTML = html;
      var sb = temp.querySelector('#sidebar');
      var old = document.getElementById('sidebar');
      if (sb && old) old.replaceWith(sb);
      var ov = temp.querySelector('#sidebarOverlay');
      var oldOv = document.getElementById('sidebarOverlay');
      if (ov) { if (oldOv) oldOv.remove(); document.body.appendChild(ov); }

      var path = location.pathname;
      document.querySelectorAll('.user-item').forEach(function(el) {
        el.classList.toggle('active', el.getAttribute('href') === path);
      });
      syncThemePills();
      applyPresence();
      loadStatusStrip();
    })
    .catch(function(e) { console.error('AeroChat: no se pudo refrescar el sidebar', e); });
}

// ── Friend actions ──
function sendFriendRequest(userId) { acInvoke('SendFriendRequest', userId); }
function acceptRequest(fromId, reqId, btn) {
  if (btn) btn.disabled = true;
  acInvoke('AcceptFriendRequest', reqId, fromId);
}
function declineRequest(fromId, reqId, btn) {
  if (btn) btn.disabled = true;
  acInvoke('DeclineFriendRequest', reqId, fromId);
}
function cancelRequest(toId) { acInvoke('CancelFriendRequest', toId); }
function removeFriend(friendId) {
  if (!confirm('¿Eliminar a este amigo de tu lista?')) return;
  acInvoke('RemoveFriend', friendId);
}

// ── Profile friend actions (re-render tras eventos) ──
function renderProfileFriendActions(state) {
  var wrap = document.getElementById('profileFriendActions');
  if (!wrap || wrap.getAttribute('data-isown') === '1') return;
  var id = wrap.getAttribute('data-userid');
  var reqId = wrap.getAttribute('data-requestid') || '';
  var html = '';
  if (state === 'friends') {
    html = '<a href="/Chat/Conversation/' + id + '" class="btn btn-primary">💬 Mensaje</a>'
         + '<button type="button" class="btn btn-secondary" onclick="removeFriend(\'' + id + '\')">Eliminar amigo</button>';
  } else if (state === 'incoming') {
    html = '<button type="button" class="btn btn-primary" onclick="acceptRequest(\'' + id + '\',\'' + reqId + '\',this)">✓ Aceptar solicitud</button>'
         + '<button type="button" class="btn btn-ghost" onclick="declineRequest(\'' + id + '\',\'' + reqId + '\',this)">Rechazar</button>';
  } else if (state === 'outgoing') {
    html = '<button type="button" class="btn btn-secondary" disabled>Solicitud enviada</button>'
         + '<button type="button" class="btn btn-ghost" onclick="cancelRequest(\'' + id + '\')">Cancelar</button>';
  } else {
    html = '<button type="button" class="btn btn-primary" onclick="sendFriendRequest(\'' + id + '\')">＋ Agregar amigo</button>';
  }
  wrap.innerHTML = html;
}

// ── SignalR hub global ──
function acInvoke(method) {
  var hub = window.acHub;
  if (!hub) return;
  var args = Array.prototype.slice.call(arguments, 1);
  hub.invoke.apply(hub, [method].concat(args)).catch(function(e) {
    console.error('AeroChat:', e);
  });
}

(function initHub() {
  var uid = document.body.getAttribute('data-userid');
  if (!uid) return;

  var hub = new signalR.HubConnectionBuilder()
    .withUrl('/chatHub')
    .withAutomaticReconnect()
    .build();
  window.acHub = hub;

  hub.on('OnlineUsers', function(ids) {
    window.acOnlineUsers = {};
    (ids || []).forEach(function(id) { window.acOnlineUsers[id] = 1; });
    applyPresence();
  });
  hub.on('UserOnline', markOnline);
  hub.on('UserOffline', markOffline);

  hub.on('FriendRequestReceived', function(sender) {
    showToast(sender.displayName + ' te envió una solicitud de amistad.', 'info');
    var wrap = document.getElementById('profileFriendActions');
    if (wrap && wrap.getAttribute('data-userid') === sender.id) {
      location.reload();
      return;
    }
    refreshSidebar();
  });
  hub.on('FriendRequestAccepted', function(friend) {
    showToast(friend.displayName + ' aceptó tu solicitud de amistad.', 'success');
    refreshSidebar().then(function() {
      var wrap = document.getElementById('profileFriendActions');
      if (wrap && wrap.getAttribute('data-userid') === friend.id) renderProfileFriendActions('friends');
    });
  });
  hub.on('FriendRequestDeclined', function(me) {
    showToast(me.displayName + ' rechazó tu solicitud.');
    refreshSidebar().then(function() {
      var wrap = document.getElementById('profileFriendActions');
      if (wrap && wrap.getAttribute('data-userid') === me.id) renderProfileFriendActions('none');
    });
  });
  hub.on('FriendRequestAcceptedSelf', function(friendId) {
    showToast('Solicitud aceptada. ¡Ahora son amigos!', 'success');
    refreshSidebar();
    var wrap = document.getElementById('profileFriendActions');
    if (wrap && wrap.getAttribute('data-userid') === friendId) renderProfileFriendActions('friends');
  });
  hub.on('FriendRequestDeclinedSelf', function(friendId) {
    showToast('Solicitud rechazada.');
    refreshSidebar();
    var wrap = document.getElementById('profileFriendActions');
    if (wrap && wrap.getAttribute('data-userid') === friendId) renderProfileFriendActions('none');
  });
  hub.on('FriendRequestSent', function(toId) {
    showToast('Solicitud de amistad enviada.', 'success');
    refreshSidebar();
    var wrap = document.getElementById('profileFriendActions');
    if (wrap && wrap.getAttribute('data-userid') === toId) renderProfileFriendActions('outgoing');
  });
  hub.on('FriendRequestCancelled', function(toId) {
    showToast('Solicitud cancelada.');
    refreshSidebar();
    var wrap = document.getElementById('profileFriendActions');
    if (wrap && wrap.getAttribute('data-userid') === toId) renderProfileFriendActions('none');
  });
  hub.on('FriendRequestError', function(reason) {
    if (reason === 'pending') showToast('Ya le enviaste una solicitud.');
    else if (reason === 'friends') showToast('Ya son amigos.');
    else showToast('No se pudo enviar la solicitud.');
    refreshSidebar();
  });
  hub.on('FriendRemoved', function(id) {
    showToast('Te eliminaron de amigos.');
    refreshSidebar();
  });
  hub.on('FriendRemovedSelf', function(friendId) {
    showToast('Amigo eliminado.');
    refreshSidebar();
    var wrap = document.getElementById('profileFriendActions');
    if (wrap && wrap.getAttribute('data-userid') === friendId) renderProfileFriendActions('none');
  });

  hub.on('GroupCreatedSelf', function(group) {
    showToast('Grupo creado.', 'success');
    location.href = '/Group/Conversation/' + group.id;
  });
  hub.on('GroupCreated', function(payload) {
    showToast((payload && payload.creatorName ? payload.creatorName + ' te ' : 'Te ') + 'agregó a un grupo.');
    refreshSidebar();
  });
  hub.on('GroupMemberLeft', function() { refreshSidebar(); });
  hub.on('GroupLeft', function() { refreshSidebar(); });
  hub.on('StatusChanged', function(name) {
    showToast((name || 'Un amigo') + ' publicó un estado.', 'info');
    loadStatusStrip();
  });

  function onConnected() {
    document.dispatchEvent(new Event('ac:hubconnected'));
    hub.invoke('GetOnlineUsers').catch(function() {});
    loadStatusStrip();
  }
  hub.onreconnected(onConnected);
  hub.start().then(onConnected).catch(function(err) {
    console.error('AeroChat: no se pudo conectar al hub', err);
  });
})();

// ── Global modal helpers ──
function openModal(id) { document.getElementById(id)?.classList.add('open'); }
function closeModal(id) { document.getElementById(id)?.classList.remove('open'); }
function openEdit(id, content) {
  document.getElementById('editMsgId').value = id;
  document.getElementById('editContent').value = content;
  openModal('editModal');
}
function confirmDelete(msgId) {
  document.getElementById('deleteMsgId').value = msgId;
  openModal('deleteModal');
}
function openLightbox(img) {
  document.getElementById('lightboxImg').src = img.src;
  document.getElementById('lightbox').classList.add('open');
}
function closeLightbox() { document.getElementById('lightbox').classList.remove('open'); }
document.addEventListener('click', function(e) {
  if (e.target.classList && e.target.classList.contains('modal-overlay')) {
    e.target.classList.remove('open');
  }
});

// ── New group ──
function openNewGroup() {
  var list = document.getElementById('newGroupMembers');
  if (!list) return;
  list.innerHTML = 'Cargando amigos…';
  closeModal('newGroupModal');
  fetch('/Home/GetFriendsJson')
    .then(function(r) { if (!r.ok) throw new Error(); return r.json(); })
    .then(function(friends) {
      if (!friends.length) {
        list.innerHTML = '<div class="sidebar-empty">No tenés amigos todavía.<br/>Agregá amigos para crear un grupo.</div>';
      } else {
        list.innerHTML = friends.map(function(f) {
          var color = f.avatarColor || '#6C63FF';
          var img = f.avatarPath ? '<img src="' + f.avatarPath + '" class="avatar avatar-sm" alt=""/>' : '<span class="avatar avatar-sm" style="background:' + color + '">' + escapeHtml(f.displayName).charAt(0) + '</span>';
          return '<label class="group-pick-item">'
            + '<input type="checkbox" value="' + f.id + '"/>'
            + '<span class="avatar-wrap">' + img + '</span>'
            + '<span class="group-pick-name">' + escapeHtml(f.displayName) + '</span>'
            + '</label>';
        }).join('');
      }
      openModal('newGroupModal');
    })
    .catch(function() {
      list.innerHTML = '<div class="sidebar-empty">No se pudieron cargar tus amigos.</div>';
      openModal('newGroupModal');
    });
}
function createGroup() {
  var name = (document.getElementById('newGroupName').value || '').trim();
  var ids = Array.prototype.slice.call(document.querySelectorAll('#newGroupMembers input:checked'))
    .map(function(c) { return c.value; });
  if (!name) { showToast('Escribí un nombre para el grupo.'); return; }
  acInvoke('CreateGroup', name, ids);
}

// ── Status strip ──
function openNewStatus() { openModal('newStatusModal'); }
function statusAvatar(name, color, path) {
  if (path) return '<img src="' + path + '" class="avatar avatar-md" alt=""/>';
  return '<span class="avatar avatar-md" style="background:' + color + '">' + escapeHtml((name || '?').charAt(0).toUpperCase()) + '</span>';
}
function loadStatusStrip() {
  var wrap = document.getElementById('statusStrip');
  if (!wrap) return;
  fetch('/Status/Summary')
    .then(function(r) { if (!r.ok) throw new Error('status'); return r.json(); })
    .then(function(data) {
      var html = '<a class="status-strip-item" href="/Status/Index" title="Mi estado">'
        + '<span class="status-ring' + (data.me.hasStatus ? ' has-status' : '') + '">'
        + statusAvatar(data.me.name, data.me.color, data.me.avatar)
        + '<span class="status-strip-add">＋</span></span>'
        + '<span class="status-strip-name">Mi estado</span></a>';
      (data.friends || []).forEach(function(f) {
        html += '<a class="status-strip-item" href="/Status/Index?u=' + f.userId + '" title="' + escapeHtml(f.name) + ': ' + escapeHtml(f.preview || '') + '">'
          + '<span class="status-ring has-status">' + statusAvatar(f.name, f.color, f.avatar) + '</span>'
          + '<span class="status-strip-name">' + escapeHtml(f.name) + '</span></a>';
      });
      if (!data.friends.length && !data.me.hasStatus) {
        html += '<div class="status-strip-hint">Tus estados y los de tus amigos aparecen acá</div>';
      }
      wrap.innerHTML = html;
    })
    .catch(function(e) { console.error('AeroChat: no se pudo cargar estados', e); });
}

// ── Calls (WebRTC mesh: audio/video, 1:1 and groups) ──
window.acCall = {
  roomId: null, type: null, mode: null, myId: null,
  groupCall: false, groupId: null, groupName: null,
  peerId: null, peerName: '', peerAvatar: '', peerColor: '#6C63FF',
  stream: null, muted: false, camOff: false,
  peers: {}, memberInfo: {},
  timerInt: null, startTs: 0,
  ringCtx: null, ringOsc: null, ringInt: null,
  outgoingTimer: null, incoming: null
};
const AC_RTC_CONFIG = { iceServers: [{ urls: 'stun:stun.l.google.com:19302' }] };

function setCallUi(state) { var el = document.getElementById('callState'); if (el) el.textContent = state; }
function setCallTimer(show) { document.getElementById('callTimer').hidden = !show; }
function updateCallButtons() {
  var ov = document.getElementById('callOverlay');
  if (!ov) return;
  ov.classList.toggle('incoming', window.acCall.mode === 'incoming');
  ov.classList.toggle('active', window.acCall.mode === 'active');
  ov.classList.toggle('video', window.acCall.type === 'video');
}
function showCallOverlay() {
  var ov = document.getElementById('callOverlay');
  if (!ov) return;
  ov.hidden = false;
  requestAnimationFrame(function() { ov.classList.add('show'); });
}
function hideCallOverlay() {
  var ov = document.getElementById('callOverlay');
  if (!ov) return;
  ov.classList.remove('show');
  setTimeout(function() { if (!ov.classList.contains('show')) ov.hidden = true; }, 300);
}

function mediaErrorToast(err) {
  if (err && err.name === 'NotAllowedError') showToast('Micrófono/cámara no permitido.');
  else if (err && err.name === 'NotFoundError') showToast('No se encontró micrófono o cámara.');
  else showToast('No se pudo acceder a los dispositivos.');
}
function acquireMedia() {
  var wantsVideo = window.acCall.type === 'video';
  return navigator.mediaDevices.getUserMedia({ audio: true, video: wantsVideo })
    .catch(function(err) {
      if (wantsVideo && err && err.name !== 'NotAllowedError') {
        return navigator.mediaDevices.getUserMedia({ audio: true, video: false });
      }
      throw err;
    });
}

function sendCallSignal(remoteId, msg) {
  if (!window.acCall.roomId || !remoteId) return;
  acInvoke('CallSignalRoom', window.acCall.roomId, remoteId, msg);
}

function addLocalTracksToPeer(peer) {
  if (peer.tracksAdded || !window.acCall.stream) return;
  peer.tracksAdded = true;
  window.acCall.stream.getTracks().forEach(function(t) { peer.pc.addTrack(t, window.acCall.stream); });
}
function syncLocalTracks() {
  Object.keys(window.acCall.peers).forEach(function(id) {
    addLocalTracksToPeer(window.acCall.peers[id]);
  });
}

function createPeer(remoteId) {
  if (window.acCall.peers[remoteId]) return window.acCall.peers[remoteId];
  var peer = {
    polite: String(window.acCall.myId) < String(remoteId),
    makingOffer: false,
    tracksAdded: false,
    pc: new RTCPeerConnection(AC_RTC_CONFIG),
    queue: []
  };
  window.acCall.peers[remoteId] = peer;
  addLocalTracksToPeer(peer);
  var pc = peer.pc;
  pc.onicecandidate = function(ev) {
    if (!ev.candidate) return;
    sendCallSignal(remoteId, {
      type: 'candidate', candidate: ev.candidate.candidate,
      sdpMid: ev.candidate.sdpMid, sdpMLineIndex: ev.candidate.sdpMLineIndex
    });
  };
  pc.ontrack = function(ev) {
    if (ev.streams && ev.streams[0]) attachRemoteStream(remoteId, ev.streams[0]);
  };
  pc.onconnectionstatechange = function() {
    if (pc.connectionState === 'failed' || pc.connectionState === 'closed') {
      delete window.acCall.peers[remoteId];
      var t = document.getElementById('callTile_' + remoteId);
      if (t) t.remove();
    }
  };
  return peer;
}

function makeOffer(remoteId) {
  var peer = window.acCall.peers[remoteId];
  if (!peer || !peer.pc) return;
  peer.makingOffer = true;
  peer.pc.createOffer()
    .then(function(offer) { return peer.pc.setLocalDescription(offer); })
    .then(function() { sendCallSignal(remoteId, { type: 'offer', sdp: peer.pc.localDescription.sdp }); })
    .catch(function(e) { console.error('AeroChat: offer', e); })
    .finally(function() { peer.makingOffer = false; });
}

function flushPeerQueue(peer) {
  peer.queue.forEach(function(msg) {
    peer.pc.addIceCandidate(new RTCIceCandidate({
      candidate: msg.candidate, sdpMid: msg.sdpMid, sdpMLineIndex: msg.sdpMLineIndex
    })).catch(function(e) { console.error('AeroChat: ice flush', e); });
  });
  peer.queue = [];
}

function handleCallSignal(from, msg) {
  if (!msg || !msg.type || from === window.acCall.myId) return;
  var peer = createPeer(from);
  var pc = peer.pc;
  if (msg.type === 'offer') {
    var collision = peer.makingOffer || pc.signalingState !== 'stable';
    if (collision && !peer.polite) return;
    if (collision && pc.signalingState === 'have-local-offer') {
      pc.setLocalDescription({ type: 'rollback' });
    }
    pc.setRemoteDescription(new RTCSessionDescription({ type: 'offer', sdp: msg.sdp }))
      .then(function() { flushPeerQueue(peer); })
      .then(function() { return pc.createAnswer(); })
      .then(function(answer) { return pc.setLocalDescription(answer); })
      .then(function() { sendCallSignal(from, { type: 'answer', sdp: pc.localDescription.sdp }); })
      .catch(function(e) { console.error('AeroChat: answer', e); });
  } else if (msg.type === 'answer') {
    if (pc.signalingState === 'stable') return;
    pc.setRemoteDescription(new RTCSessionDescription({ type: 'answer', sdp: msg.sdp }))
      .then(function() { flushPeerQueue(peer); })
      .catch(function(e) { console.error('AeroChat: setRemote answer', e); });
  } else if (msg.type === 'candidate' && msg.candidate) {
    if (!pc.remoteDescription) { peer.queue.push(msg); return; }
    pc.addIceCandidate(new RTCIceCandidate({
      candidate: msg.candidate, sdpMid: msg.sdpMid, sdpMLineIndex: msg.sdpMLineIndex
    })).catch(function(e) { console.error('AeroChat: ice', e); });
  }
}

function startRingtone() {
  if (window.acCall.ringCtx) return;
  var ctx = new (window.AudioContext || window.webkitAudioContext)();
  window.acCall.ringCtx = ctx;
  var osc = ctx.createOscillator();
  var gain = ctx.createGain();
  osc.type = 'sine';
  osc.frequency.value = 620;
  gain.gain.value = 0.06;
  osc.connect(gain); gain.connect(ctx.destination);
  osc.start();
  window.acCall.ringOsc = osc;
  var on = true;
  window.acCall.ringInt = setInterval(function() {
    on = !on;
    gain.gain.value = on ? 0.06 : 0;
  }, 350);
}
function stopRingtone() {
  if (window.acCall.ringInt) clearInterval(window.acCall.ringInt);
  if (window.acCall.ringOsc) { try { window.acCall.ringOsc.stop(); } catch (e) {} }
  if (window.acCall.ringCtx) { try { window.acCall.ringCtx.close(); } catch (e) {} }
  window.acCall.ringInt = null; window.acCall.ringOsc = null; window.acCall.ringCtx = null;
}

function startTimer() {
  window.acCall.startTs = Date.now();
  if (window.acCall.timerInt) clearInterval(window.acCall.timerInt);
  window.acCall.timerInt = setInterval(function() {
    var s = Math.floor((Date.now() - window.acCall.startTs) / 1000);
    document.getElementById('callTimer').textContent =
      String(Math.floor(s / 60)).padStart(2, '0') + ':' + String(s % 60).padStart(2, '0');
  }, 1000);
}

function clearCallVideos() {
  var grid = document.getElementById('callVideos');
  if (grid) grid.innerHTML = '';
}
function localTile() {
  var grid = document.getElementById('callVideos');
  if (!grid) return null;
  var t = grid.querySelector('.call-video-tile.local');
  if (t) return t;
  t = document.createElement('div');
  t.className = 'call-video-tile local';
  var v = document.createElement('video');
  v.autoplay = true; v.muted = true; v.playsInline = true;
  t.appendChild(v);
  grid.insertBefore(t, grid.firstChild);
  return t;
}
function renderPeerVideo(remoteId) {
  var grid = document.getElementById('callVideos');
  if (!grid) return null;
  var t = document.createElement('div');
  t.className = 'call-video-tile remote';
  t.id = 'callTile_' + remoteId;
  var v = document.createElement('video');
  v.autoplay = true; v.playsInline = true;
  t.appendChild(v);
  grid.appendChild(t);
  return t;
}
function attachLocalStream(stream) {
  if (window.acCall.type !== 'video' || !stream) return;
  var t = localTile();
  if (!t) return;
  var v = t.querySelector('video');
  v.srcObject = stream;
  v.play().catch(function(e) { console.error('AeroChat: play local', e); });
}
function attachRemoteStream(remoteId, stream) {
  if (!stream) return;
  var t = document.getElementById('callTile_' + remoteId);
  if (!t) t = renderPeerVideo(remoteId);
  if (!t) return;
  var v = t.querySelector('video');
  v.srcObject = stream;
  v.play().catch(function(e) { console.error('AeroChat: play remoto', e); });
}
function clearParticipants() {
  var p = document.getElementById('callParticipants');
  if (p) p.innerHTML = '';
}
function renderParticipants() {
  var p = document.getElementById('callParticipants');
  if (!p) return;
  p.innerHTML = '';
  Object.keys(window.acCall.memberInfo).forEach(function(id) {
    var m = window.acCall.memberInfo[id];
    var el = document.createElement('span');
    el.className = 'participant-chip';
    el.title = m.displayName || '';
    el.textContent = m.displayName ? m.displayName.charAt(0).toUpperCase() : '?';
    p.appendChild(el);
  });
}

function setupCallDisplay(peerId, name, avatar, color, state, type) {
  window.acCall.myId = document.body.getAttribute('data-userid') || '';
  window.acCall.peerId = peerId;
  window.acCall.peerName = name || '';
  window.acCall.peerAvatar = avatar || '';
  window.acCall.peerColor = color || '#6C63FF';
  window.acCall.type = type || 'audio';
  var av = document.getElementById('callAvatar');
  av.style.background = window.acCall.peerColor;
  av.textContent = name ? name.charAt(0).toUpperCase() : '?';
  document.getElementById('callName').textContent = name || '…';
  setCallUi(state);
  showCallOverlay();
  updateCallButtons();
}

function cleanupCall() {
  stopRingtone();
  if (window.acCall.timerInt) clearInterval(window.acCall.timerInt);
  window.acCall.timerInt = null;
  if (window.acCall.outgoingTimer) clearTimeout(window.acCall.outgoingTimer);
  window.acCall.outgoingTimer = null;
  Object.keys(window.acCall.peers).forEach(function(id) {
    try { window.acCall.peers[id].pc.close(); } catch (e) {}
  });
  window.acCall.peers = {};
  window.acCall.memberInfo = {};
  if (window.acCall.stream) {
    window.acCall.stream.getTracks().forEach(function(t) { t.stop(); });
  }
  window.acCall.stream = null;
  window.acCall.mode = null;
  window.acCall.roomId = null;
  window.acCall.type = null;
  window.acCall.groupCall = false;
  window.acCall.groupId = null;
  window.acCall.groupName = null;
  window.acCall.peerId = null;
  window.acCall.peerName = '';
  window.acCall.muted = false;
  window.acCall.camOff = false;
  window.acCall.incoming = null;
  var muteBtn = document.getElementById('callMute');
  if (muteBtn) { muteBtn.classList.remove('muted'); muteBtn.textContent = '🎙'; }
  var camBtn = document.getElementById('callCam');
  if (camBtn) { camBtn.classList.remove('off'); camBtn.textContent = '🎥'; }
  setCallTimer(false);
  clearCallVideos();
  clearParticipants();
  updateCallButtons();
  hideCallOverlay();
}

function endCall(reason) {
  if (window.acCall.mode && window.acCall.roomId) acInvoke('LeaveCallRoom', window.acCall.roomId);
  if (reason) showToast(reason);
  cleanupCall();
}

function hangupCall() {
  if (!window.acCall.mode) return;
  if (window.acCall.mode === 'incoming') {
    acInvoke('DeclineCall', window.acCall.roomId);
  } else if (window.acCall.roomId) {
    acInvoke('LeaveCallRoom', window.acCall.roomId);
  }
  cleanupCall();
}

function declineIncoming() {
  if (window.acCall.mode !== 'incoming') return;
  acInvoke('DeclineCall', window.acCall.roomId);
  cleanupCall();
}

function toggleMute() {
  if (!window.acCall.stream) return;
  window.acCall.muted = !window.acCall.muted;
  window.acCall.stream.getAudioTracks().forEach(function(t) { t.enabled = !window.acCall.muted; });
  var btn = document.getElementById('callMute');
  btn.classList.toggle('muted', window.acCall.muted);
  btn.textContent = window.acCall.muted ? '🔇' : '🎙';
}

function toggleCam() {
  if (window.acCall.type !== 'video' || !window.acCall.stream) return;
  window.acCall.camOff = !window.acCall.camOff;
  window.acCall.stream.getVideoTracks().forEach(function(t) { t.enabled = !window.acCall.camOff; });
  var btn = document.getElementById('callCam');
  btn.classList.toggle('off', window.acCall.camOff);
  btn.textContent = window.acCall.camOff ? '🚫' : '🎥';
}

function callFriend(peerId, name, avatar, color, type) {
  if (window.acCall.mode) { showToast('Ya hay una llamada en curso.'); return; }
  if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    showToast('Llamadas requieren HTTPS o localhost.');
    return;
  }
  type = type || 'audio';
  setupCallDisplay(peerId, name, avatar, color, 'Llamando…', type);
  window.acCall.mode = 'outgoing';
  updateCallButtons();
  window.acCall.outgoingTimer = setTimeout(function() {
    if (window.acCall.mode === 'outgoing') endCall('La persona no respondió.');
  }, 30000);
  acquireMedia()
    .then(function(stream) {
      window.acCall.stream = stream;
      attachLocalStream(stream);
      syncLocalTracks();
      acInvoke('StartCall', peerId, type);
    })
    .catch(function(err) {
      mediaErrorToast(err);
      cleanupCall();
    });
}

function callGroup(groupId, groupName, groupColor, type) {
  if (window.acCall.mode) { showToast('Ya hay una llamada en curso.'); return; }
  if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    showToast('Llamadas requieren HTTPS o localhost.');
    return;
  }
  type = type || 'audio';
  setupCallDisplay(groupId, groupName, '', groupColor, 'Llamando…', type);
  window.acCall.groupCall = true;
  window.acCall.groupId = groupId;
  window.acCall.groupName = groupName || '';
  window.acCall.mode = 'outgoing';
  updateCallButtons();
  window.acCall.outgoingTimer = setTimeout(function() {
    if (window.acCall.mode === 'outgoing') endCall('Nadie respondió.');
  }, 60000);
  acquireMedia()
    .then(function(stream) {
      window.acCall.stream = stream;
      attachLocalStream(stream);
      syncLocalTracks();
      acInvoke('CreateGroupCall', groupId, type);
    })
    .catch(function(err) {
      mediaErrorToast(err);
      cleanupCall();
    });
}

function acceptIncoming() {
  if (window.acCall.mode !== 'incoming') return;
  window.acCall.mode = 'joining';
  updateCallButtons();
  setCallUi('Conectando…');
  acquireMedia()
    .then(function(stream) {
      window.acCall.stream = stream;
      attachLocalStream(stream);
      syncLocalTracks();
      acInvoke('JoinCallRoom', window.acCall.roomId);
    })
    .catch(function(err) {
      mediaErrorToast(err);
      window.acCall.mode = 'incoming';
      updateCallButtons();
      setCallUi('Llamada entrante…');
    });
}

function openCallInvite() {
  var modal = document.getElementById('callInviteModal');
  var list = document.getElementById('callInviteList');
  if (!modal || !list) return;
  modal.hidden = false;
  list.innerHTML = 'Cargando amigos…';
  fetch('/Home/GetFriendsJson')
    .then(function(r) { if (!r.ok) throw new Error(); return r.json(); })
    .then(function(friends) {
      var inCall = Object.keys(window.acCall.peers);
      if (window.acCall.peerId) inCall.push(window.acCall.peerId);
      var eligible = friends.filter(function(f) { return inCall.indexOf(f.id) < 0; });
      if (!eligible.length) {
        list.innerHTML = '<div class="sidebar-empty">No hay amigos para invitar.</div>';
        return;
      }
      list.innerHTML = eligible.map(function(f) {
        var color = f.avatarColor || '#6C63FF';
        var img = f.avatarPath
          ? '<img src="' + f.avatarPath + '" class="avatar avatar-sm" alt=""/>'
          : '<span class="avatar avatar-sm" style="background:' + color + '">' + escapeHtml(f.displayName).charAt(0) + '</span>';
        return '<div class="call-invite-item" onclick="sendCallInvite(\'' + f.id + '\')">'
          + '<span class="avatar-wrap">' + img + '</span>'
          + '<span class="group-pick-name">' + escapeHtml(f.displayName) + '</span>'
          + '<span class="call-invite-go">→</span>'
          + '</div>';
      }).join('');
    })
    .catch(function() {
      list.innerHTML = '<div class="sidebar-empty">No se pudieron cargar tus amigos.</div>';
    });
}

function sendCallInvite(friendId) {
  var modal = document.getElementById('callInviteModal');
  if (modal) modal.hidden = true;
  acInvoke('InviteToCall', friendId, window.acCall.roomId);
}

// ── Call hub handlers ──
function registerCallHandlers(hub) {
  hub.on('CallCreated', function(roomId, type, targetId) {
    if (window.acCall.mode !== 'outgoing' || !roomId) return;
    window.acCall.roomId = roomId;
    window.acCall.type = type || window.acCall.type;
    updateCallButtons();
  });

  hub.on('IncomingCall', function(payload) {
    if (!payload || !payload.roomId) return;
    if (window.acCall.mode) {
      acInvoke('DeclineCall', payload.roomId);
      return;
    }
    window.acCall.myId = document.body.getAttribute('data-userid') || '';
    window.acCall.roomId = payload.roomId;
    window.acCall.type = payload.type === 'video' ? 'video' : 'audio';
    window.acCall.groupCall = !!(payload.groupId);
    window.acCall.groupId = payload.groupId || null;
    window.acCall.groupName = payload.groupName || null;
    window.acCall.peerId = payload.fromId;
    window.acCall.peerName = payload.fromName || '';
    window.acCall.peerAvatar = payload.fromAvatar || '';
    window.acCall.peerColor = payload.fromColor || '#6C63FF';
    window.acCall.incoming = payload;
    setupCallDisplay(window.acCall.peerId, window.acCall.peerName, window.acCall.peerAvatar, window.acCall.peerColor, 'Llamada entrante…', window.acCall.type);
    if (window.acCall.groupCall) {
      document.getElementById('callName').textContent = window.acCall.groupName || 'Llamada de grupo';
    }
    window.acCall.mode = 'incoming';
    updateCallButtons();
    startRingtone();
    showToast((window.acCall.peerName || 'Alguien') + ' te está llamando.', 'info');
  });

  hub.on('CallRoomJoined', function(roomId, members) {
    if (window.acCall.roomId !== roomId) return;
    window.acCall.mode = 'active';
    updateCallButtons();
    setCallUi('En llamada');
    setCallTimer(true);
    startTimer();
    stopRingtone();
    window.acCall.memberInfo = {};
    (members || []).forEach(function(m) { if (m && m.userId) window.acCall.memberInfo[m.userId] = m; });
    renderParticipants();
    (members || []).forEach(function(m) {
      if (!m || m.userId === window.acCall.myId) return;
      createPeer(m.userId);
      makeOffer(m.userId);
    });
  });

  hub.on('CallUserJoined', function(member) {
    if (!window.acCall.roomId || !member || !member.userId) return;
    window.acCall.memberInfo[member.userId] = member;
    renderParticipants();
    if (window.acCall.mode === 'outgoing') {
      window.acCall.mode = 'active';
      updateCallButtons();
      setCallUi('En llamada');
      setCallTimer(true);
      startTimer();
      if (window.acCall.outgoingTimer) { clearTimeout(window.acCall.outgoingTimer); window.acCall.outgoingTimer = null; }
    }
    if (member.userId === window.acCall.myId) return;
    createPeer(member.userId);
    makeOffer(member.userId);
  });

  hub.on('CallRoomSignal', function(payload) {
    if (!payload || payload.roomId !== window.acCall.roomId) return;
    handleCallSignal(payload.from, payload.message);
  });

  hub.on('CallUserLeft', function(uid, roomId) {
    if (roomId !== window.acCall.roomId) return;
    delete window.acCall.memberInfo[uid];
    renderParticipants();
    var peer = window.acCall.peers[uid];
    if (peer) { try { peer.pc.close(); } catch (e) {} }
    delete window.acCall.peers[uid];
    var t = document.getElementById('callTile_' + uid);
    if (t) t.remove();
  });

  hub.on('CallCancelled', function(roomId) {
    if (window.acCall.roomId !== roomId) return;
    cleanupCall();
    showToast('Llamada cancelada.');
  });

  hub.on('CallDeclined', function(uid, roomId) {
    if (window.acCall.roomId !== roomId) return;
    if (window.acCall.groupCall) {
      showToast('Un participante rechazó la llamada.');
    } else if (uid === window.acCall.peerId) {
      endCall('Llamada rechazada.');
    }
  });

  hub.on('CallBusy', function(uid) {
    if (window.acCall.groupCall) { showToast('La persona está en otra llamada.'); return; }
    if (window.acCall.peerId === uid && window.acCall.mode === 'outgoing') {
      endCall('La persona está en otra llamada.');
    }
  });

  hub.on('CallOffline', function(uid) {
    if (window.acCall.groupCall) { showToast('La persona no está en línea.'); return; }
    if (window.acCall.peerId === uid && window.acCall.mode === 'outgoing') {
      endCall('La persona no está en línea.');
    }
  });

  hub.on('CallEnded', function(roomId) {
    if (window.acCall.roomId === roomId) {
      cleanupCall();
      showToast('La llamada finalizó.');
    }
  });
}

// ── Attach call handlers (single registration) ──
(function() {
  if (window.acHub) registerCallHandlers(window.acHub);
})();

function escapeHtml(s) {
  var d = document.createElement('div');
  d.textContent = s == null ? '' : String(s);
  return d.innerHTML;
}
