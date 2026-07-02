// AeroChat — global JS
// Sidebar mobile controls
function openSidebar() {
  document.getElementById('sidebar')?.classList.add('open');
  document.getElementById('sidebarOverlay')?.classList.add('open');
}
function closeSidebar() {
  document.getElementById('sidebar')?.classList.remove('open');
  document.getElementById('sidebarOverlay')?.classList.remove('open');
}

// Apply persisted theme on load
(function() {
  var t = localStorage.getItem('ac-theme') || 'dark';
  document.documentElement.setAttribute('data-theme', t);
  if (t === 'light') {
    document.querySelector('.theme-pill-dark')?.classList.remove('active');
    document.querySelector('.theme-pill-light')?.classList.add('active');
  }
})();
