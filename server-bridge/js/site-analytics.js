/**
 * Website analytics for server-bridge.com and leecrowesoftware.com (PostHog, US Cloud).
 *
 * Same file on both sites; the page's <script data-site="..."> says which site it is, and every
 * event carries that as `site` so the two (and gethalera.com, which shares the PostHog project)
 * can be filtered apart.
 *
 * Cookieless, in-memory only, no autocapture, no session replay, no person profiles. What is sent:
 *   $pageview         — with the full URL, so utm_* and ct= campaign codes survive
 *   checkout_click    — a Stripe checkout link was clicked, with the tier
 *   download_click    — a GitHub download or release link was clicked, with which product
 * Plus `is_bot` on everything, so crawlers can be filtered out.
 *
 * The project token is a placeholder here and is filled in at deploy time by the Static Web Apps
 * workflow from the POSTHOG_PROJECT_API_KEY repo secret. phc_ tokens are public by design (every
 * visitor's browser receives one); we still keep it out of git. With no token, this does nothing.
 *
 * posthog.js is self-hosted from /assets/ so visitors don't load a third-party script CDN.
 */
(function () {
  var TOKEN = "__POSTHOG_PROJECT_API_KEY__";
  if (!TOKEN || TOKEN.indexOf("__POSTHOG_") === 0) return;

  var me = document.currentScript;
  var SITE = (me && me.getAttribute("data-site")) || location.hostname;
  var BOT_UA = /bot|crawl|spider|slurp|preview|facebookexternalhit|embedly|lighthouse|headless/i;

  function clean(v) {
    return String(v || "").replace(/[^A-Za-z0-9_-]/g, "").slice(0, 40);
  }
  function campaign() {
    try {
      var q = new URLSearchParams(location.search);
      return clean(q.get("ct") || q.get("utm_campaign") || "");
    } catch (e) {
      return "";
    }
  }

  function linkEvent(a) {
    var href = a.href || "";
    if (href.indexOf("buy.stripe.com") !== -1) {
      return ["checkout_click", { tier: clean((a.textContent || "").replace(/^\s*Buy\s*/i, "")).toLowerCase() || "unknown" }];
    }
    if (/github\.com\/lbcrowe-del\/[^/]+\/releases/.test(href) || /powershellgallery\.com/.test(href)) {
      var m = href.match(/github\.com\/lbcrowe-del\/([^/]+)/);
      return ["download_click", { product: m ? m[1] : "ServerBridge.LicenseScan" }];
    }
    return null;
  }

  function boot() {
    var ph = window.posthog;
    if (!ph || typeof ph.init !== "function") return;
    var ct = campaign();

    ph.init(TOKEN, {
      api_host: "https://us.i.posthog.com",
      ui_host: "https://us.posthog.com",
      cookieless_mode: "always",
      persistence: "memory",
      person_profiles: "never",
      autocapture: false,
      disable_session_recording: true,
      capture_pageleave: false,
      // Sent by hand below, after the properties are registered, and by sendBeacon so a visitor
      // who clicks straight through to Stripe or GitHub is still counted.
      capture_pageview: false,
      loaded: function (p) {
        var props = { site: SITE, is_bot: BOT_UA.test(navigator.userAgent || "") };
        if (ct) props.ct = ct;
        p.register(props);
        p.capture("$pageview", null, { send_instantly: true, transport: "sendBeacon" });
      },
    });

    document.addEventListener(
      "click",
      function (e) {
        var a = e.target && e.target.closest ? e.target.closest("a[href]") : null;
        var ev = a && linkEvent(a);
        if (!ev) return;
        ev[1].page = location.pathname;
        try {
          window.posthog.capture(ev[0], ev[1], { send_instantly: true, transport: "sendBeacon" });
        } catch (err) {
          /* never block the link */
        }
      },
      true
    );
  }

  // posthog.js is loaded with defer just before this script, so it is normally already there.
  if (window.posthog) boot();
  else if (document.readyState === "loading") document.addEventListener("DOMContentLoaded", boot);
  else setTimeout(boot, 0);
})();
