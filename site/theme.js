// Light/dark switch for the project page. Without a stored choice the page follows the system
// (prefers-color-scheme in style.css); a click stores the opposite of what is shown now.
// Loaded in <head> without defer so a stored choice applies before the first paint.
(function () {
  var root = document.documentElement;
  var key = "a2g-patch-theme";

  function stored() {
    try { return localStorage.getItem(key); } catch (e) { return null; }
  }

  var choice = stored();
  if (choice === "light" || choice === "dark") root.setAttribute("data-theme", choice);

  function current() {
    var set = root.getAttribute("data-theme");
    if (set) return set;
    return window.matchMedia && window.matchMedia("(prefers-color-scheme: light)").matches ? "light" : "dark";
  }

  document.addEventListener("DOMContentLoaded", function () {
    var btn = document.getElementById("theme-btn");
    if (!btn) return;
    function label() {
      btn.setAttribute("aria-pressed", current() === "dark" ? "true" : "false");
    }
    btn.hidden = false;
    label();
    btn.addEventListener("click", function () {
      var next = current() === "dark" ? "light" : "dark";
      root.setAttribute("data-theme", next);
      try { localStorage.setItem(key, next); } catch (e) { }
      label();
    });
    if (window.matchMedia) {
      var mq = window.matchMedia("(prefers-color-scheme: light)");
      if (mq.addEventListener) mq.addEventListener("change", label);
    }
  });
})();
