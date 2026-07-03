/* Agentwerke — agentwerke.de
   Minimal, dependency-free progressive enhancement:
   theme toggle (persisted + system default), mobile menu, scroll reveal, year. */
(function () {
  "use strict";

  var root = document.documentElement;
  var STORAGE_KEY = "agentwerke-theme";

  /* ---- theme ---- */
  function systemPrefersLight() {
    return window.matchMedia && window.matchMedia("(prefers-color-scheme: light)").matches;
  }
  function applyTheme(theme) {
    root.setAttribute("data-theme", theme);
    var meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.setAttribute("content", theme === "light" ? "#FBFCFC" : "#0e0e0f");
  }
  // Dark is the brand default first paint; a stored user choice always wins.
  // (systemPrefersLight retained for teams that prefer to honor the OS setting.)
  try {
    var stored = localStorage.getItem(STORAGE_KEY);
    applyTheme(stored || "dark");
  } catch (e) { /* private mode */ }
  void systemPrefersLight;

  var toggle = document.getElementById("theme-toggle");
  if (toggle) {
    toggle.addEventListener("click", function () {
      var next = root.getAttribute("data-theme") === "light" ? "dark" : "light";
      applyTheme(next);
      try { localStorage.setItem(STORAGE_KEY, next); } catch (e) {}
    });
  }

  /* ---- mobile menu ---- */
  var burger = document.getElementById("nav-burger");
  var menu = document.getElementById("mobile-menu");
  if (burger && menu) {
    burger.addEventListener("click", function () {
      var open = menu.classList.toggle("is-open");
      menu.hidden = !open;
      burger.setAttribute("aria-expanded", String(open));
    });
    menu.addEventListener("click", function (e) {
      if (e.target.tagName === "A") {
        menu.classList.remove("is-open");
        menu.hidden = true;
        burger.setAttribute("aria-expanded", "false");
      }
    });
  }

  /* ---- reveal on scroll ----
     Anything already in the initial viewport is revealed immediately (no reliance
     on the observer firing, so above-the-fold content never depends on scroll);
     the rest animates in as it scrolls into view. */
  var reveals = document.querySelectorAll(".reveal");
  function revealNow(el) { el.classList.add("is-visible"); }
  if ("IntersectionObserver" in window && reveals.length) {
    var vh = window.innerHeight || document.documentElement.clientHeight;
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) { revealNow(entry.target); io.unobserve(entry.target); }
      });
    }, { threshold: 0.12, rootMargin: "0px 0px -40px 0px" });
    reveals.forEach(function (el) {
      if (el.getBoundingClientRect().top < vh) { revealNow(el); io.unobserve(el); }
      else { io.observe(el); }
    });
    // Safety net: never leave content hidden if the observer misfires.
    setTimeout(function () { reveals.forEach(revealNow); }, 1200);
  } else {
    reveals.forEach(revealNow);
  }

  /* ---- year ---- */
  var year = document.getElementById("year");
  if (year) year.textContent = String(new Date().getFullYear());
})();
