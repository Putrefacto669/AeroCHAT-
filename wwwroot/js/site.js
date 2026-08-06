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

  function onConnected() {
    document.dispatchEvent(new Event('ac:hubconnected'));
    hub.invoke('GetOnlineUsers').catch(function() {});
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

// ── Calls (WebRTC audio) ──
window.acCall = {
  mode: null, peerId: null, peerName: '', peerAvatar: '', peerColor: '#6C63FF',
  pc: null, stream: null, muted: false, timerInt: null, startTs: 0,
  pendingOffer: null, pendingCandidates: []
};
const AC_RTC_CONFIG = { iceServers: [{ urls: 'stun:stun.l.google.com:19302' }] };

function setCallUi(state) { document.getElementById('callState').textContent = state; }
function setCallTimer(show) { document.getElementById('callTimer').hidden = !show; }
function updateCallButtons() {
  var ov = document.getElementById('callOverlay');
  if (!ov) return;
  ov.classList.toggle('incoming', window.acCall.mode === 'incoming');
}
function declineIncoming() { hangupCall(); }
function showCallOverlay() {
  var ov = document.getElementById('callOverlay');
  ov.hidden = false;
  requestAnimationFrame(function() { ov.classList.add('show'); });
}
function hideCallOverlay() {
  var ov = document.getElementById('callOverlay');
  ov.classList.remove('show');
  setTimeout(function() { if (!ov.classList.contains('show')) ov.hidden = true; }, 300);
}

function makePc() {
  var pc = new RTCPeerConnection(AC_RTC_CONFIG);
  pc.onicecandidate = function(ev) {
    if (!ev.candidate || !window.acCall.peerId) return;
    sendCallMsg({ type: 'candidate', candidate: ev.candidate.candidate, sdpMid: ev.candidate.sdpMid, sdpMLineIndex: ev.candidate.sdpMLineIndex });
  };
  pc.ontrack = function(ev) {
    var el = document.getElementById('remoteAudio');
    if (!el) return;
    el.srcObject = ev.streams[0] || el.srcObject;
    el.play().catch(function(e) { console.error('AeroChat: no se pudo reproducir el audio remoto', e); });
  };
  pc.onconnectionstatechange = function() {
    if (pc.connectionState === 'failed') endCall('Conexión perdida');
  };
  if (window.acCall.stream) {
    window.acCall.stream.getTracks().forEach(function(t) { pc.addTrack(t, window.acCall.stream); });
  }
  return pc;
}

function flushPendingCandidates() {
  var pc = window.acCall.pc;
  if (!pc || !pc.remoteDescription) return;
  var list = window.acCall.pendingCandidates || [];
  window.acCall.pendingCandidates = [];
  list.forEach(function(msg) {
    pc.addIceCandidate(new RTCIceCandidate({
      candidate: msg.candidate, sdpMid: msg.sdpMid, sdpMLineIndex: msg.sdpMLineIndex
    })).catch(function(e) { console.error('AeroChat: ice flush', e); });
  });
}

function sendCallMsg(msg) { acInvoke('CallSignal', window.acCall.peerId, msg); }

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

function setupCallDisplay(peerId, name, avatar, color, state) {
  window.acCall.peerId = peerId;
  window.acCall.peerName = name || '';
  window.acCall.peerAvatar = avatar || '';
  window.acCall.peerColor = color || '#6C63FF';
  var av = document.getElementById('callAvatar');
  av.style.background = window.acCall.peerColor;
  av.textContent = name ? name.charAt(0).toUpperCase() : '?';
  document.getElementById('callName').textContent = name || '…';
  setCallUi(state);
  showCallOverlay();
}

function cleanupCall() {
  stopRingtone();
  if (window.acCall.timerInt) clearInterval(window.acCall.timerInt);
  window.acCall.timerInt = null;
  if (window.acCall.outgoingTimer) clearTimeout(window.acCall.outgoingTimer);
  window.acCall.outgoingTimer = null;
  if (window.acCall.pc) { try { window.acCall.pc.close(); } catch (e) {} }
  window.acCall.pc = null;
  if (window.acCall.stream) {
    window.acCall.stream.getTracks().forEach(function(t) { t.stop(); });
  }
  window.acCall.stream = null;
  window.acCall.mode = null;
  window.acCall.peerId = null;
  window.acCall.pendingOffer = null;
  window.acCall.pendingCandidates = [];
  document.getElementById('callMute').classList.remove('muted');
  document.getElementById('callMute').textContent = '🎙';
  setCallTimer(false);
  updateCallButtons();
  hideCallOverlay();
}

function endCall(reason) {
  if (window.acCall.mode) acInvoke('CallHangup', window.acCall.peerId);
  if (reason) showToast(reason);
  cleanupCall();
}

function hangupCall() {
  if (!window.acCall.mode) return;
  if (window.acCall.mode === 'incoming' && window.acCall.pendingOffer) {
    acInvoke('CallDecline', window.acCall.peerId);
  } else {
    acInvoke('CallHangup', window.acCall.peerId);
  }
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

function callFriend(peerId, name, avatar, color) {
  if (window.acCall.mode) { showToast('Ya hay una llamada en curso.'); return; }
  if (!navigator.mediaDevices || !navigator.mediaDevices.getUserMedia) {
    showToast('Llamadas requieren HTTPS o localhost.');
    return;
  }
  setupCallDisplay(peerId, name, avatar, color, 'Llamando…');
  window.acCall.mode = 'outgoing';
  updateCallButtons();
  window.acCall.outgoingTimer = setTimeout(function() {
    if (window.acCall.mode === 'outgoing') endCall('La persona no respondió.');
  }, 30000);
  navigator.mediaDevices.getUserMedia({ audio: true })
    .then(function(stream) {
      window.acCall.stream = stream;
      window.acCall.pc = makePc();
      return window.acCall.pc.createOffer();
    })
    .then(function(offer) {
      return window.acCall.pc.setLocalDescription(offer);
    })
    .then(function() {
      sendCallMsg({ type: 'offer', sdp: window.acCall.pc.localDescription.sdp });
    })
    .catch(function(err) {
      console.error('AeroChat: getUserMedia/offer', err);
      if (err.name === 'NotAllowedError') showToast('Micrófono no permitido.');
      else if (err.name === 'NotFoundError') showToast('No se encontró micrófono.');
      else showToast('No se pudo iniciar la llamada.');
      cleanupCall();
    });
}

function acceptIncoming() {
  if (!window.acCall.pendingOffer) return;
  window.acCall.mode = 'active';
  updateCallButtons();
  setCallUi('En llamada');
  setCallTimer(true);
  stopRingtone();
  startTimer();
  navigator.mediaDevices.getUserMedia({ audio: true })
    .then(function(stream) {
      window.acCall.stream = stream;
      window.acCall.pc = makePc();
      var offer = window.acCall.pendingOffer;
      window.acCall.pendingOffer = null;
      return window.acCall.pc.setRemoteDescription(new RTCSessionDescription({ type: 'offer', sdp: offer.sdp }))
        .then(function() { flushPendingCandidates(); })
        .then(function() { return window.acCall.pc.createAnswer(); })
        .then(function(answer) { return window.acCall.pc.setLocalDescription(answer); })
        .then(function() { sendCallMsg({ type: 'answer', sdp: window.acCall.pc.localDescription.sdp }); });
    })
    .catch(function(err) {
      console.error('AeroChat: answer', err);
      if (err.name === 'NotAllowedError') showToast('Micrófono no permitido.');
      else showToast('No se pudo aceptar la llamada.');
      hangupCall();
    });
}

// ── Call hub handlers ──
function registerCallHandlers(hub) {
  hub.on('CallSignal', function(payload) {
    if (!payload || !payload.message) return;
    var from = payload.from;
    var msg = payload.message;
    if (msg.type === 'offer') {
      if (window.acCall.mode) {
        if (window.acCall.peerId !== from) acInvoke('CallDecline', from);
        return;
      }
      setupCallDisplay(from, payload.fromName, payload.fromAvatar, payload.fromColor, 'Llamada entrante…');
      window.acCall.pendingOffer = msg;
      window.acCall.pendingCandidates = [];
      window.acCall.mode = 'incoming';
      updateCallButtons();
      startRingtone();
      showToast(payload.fromName + ' te está llamando.', 'info');
      return;
    }
    var pc = window.acCall.pc;
    if (msg.type === 'answer' && msg.sdp) {
      if (!pc || window.acCall.peerId !== from) return;
      pc.setRemoteDescription(new RTCSessionDescription({ type: 'answer', sdp: msg.sdp }))
        .then(function() { flushPendingCandidates(); })
        .catch(function(e) { console.error('AeroChat: setRemote answer', e); });
      window.acCall.mode = 'active';
      updateCallButtons();
      setCallUi('En llamada');
      setCallTimer(true);
      startTimer();
    } else if (msg.type === 'candidate' && msg.candidate) {
      if (window.acCall.peerId !== from) return;
      if (!pc || !pc.remoteDescription) {
        window.acCall.pendingCandidates.push(msg);
        return;
      }
      pc.addIceCandidate(new RTCIceCandidate({
        candidate: msg.candidate, sdpMid: msg.sdpMid, sdpMLineIndex: msg.sdpMLineIndex
      })).catch(function(e) { console.error('AeroChat: ice', e); });
    }
  });
  hub.on('CallEnded', function(uid) {
    if (window.acCall.peerId === uid) { stopRingtone(); cleanupCall(); showToast('Llamada finalizada.'); }
  });
  hub.on('CallDeclined', function(uid) {
    if (window.acCall.peerId === uid) { cleanupCall(); showToast('Llamada rechazada.'); }
  });
  hub.on('CallBusy', function(uid) {
    if (window.acCall.peerId === uid) { cleanupCall(); showToast('La persona está en otra llamada.'); }
  });
  hub.on('CallOffline', function(uid) {
    if (window.acCall.peerId === uid) { cleanupCall(); showToast('La persona no está en línea.'); }
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
