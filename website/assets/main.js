/* Autofac — autofac.de
   Minimal, dependency-free progressive enhancement:
   theme toggle (persisted + system default), mobile menu, scroll reveal, year. */
(function () {
  "use strict";

  var root = document.documentElement;
  var STORAGE_KEY = "autofac-theme";

  /* ---- theme ---- */
  function systemPrefersLight() {
    return window.matchMedia && window.matchMedia("(prefers-color-scheme: light)").matches;
  }
  function applyTheme(theme) {
    root.setAttribute("data-theme", theme);
    var meta = document.querySelector('meta[name="theme-color"]');
    if (meta) meta.setAttribute("content", theme === "light" ? "#FBFCFD" : "#0B0F14");
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

  /* ---- reveal on scroll ---- */
  var reveals = document.querySelectorAll(".reveal");
  if ("IntersectionObserver" in window && reveals.length) {
    var io = new IntersectionObserver(function (entries) {
      entries.forEach(function (entry) {
        if (entry.isIntersecting) {
          entry.target.classList.add("is-visible");
          io.unobserve(entry.target);
        }
      });
    }, { threshold: 0.12, rootMargin: "0px 0px -40px 0px" });
    reveals.forEach(function (el) { io.observe(el); });
  } else {
    reveals.forEach(function (el) { el.classList.add("is-visible"); });
  }

  /* ---- year ---- */
  var year = document.getElementById("year");
  if (year) year.textContent = String(new Date().getFullYear());
})();
