/**
 * Contact form on leecrowesoftware.com.
 *
 * The site is a Free Static Web App, which refuses POSTs, so the form posts to the licensing API
 * (api.server-bridge.com/api/contact, ContactFunction), which emails hello@ through Brevo.
 *
 * Without JavaScript the form still works: it posts normally and the API redirects back here
 * with ?sent=1 or ?sent=0. With JavaScript it sends form-encoded data (no CORS preflight needed)
 * and asks for JSON so it can show the result in place.
 *
 * "elapsed" tells the API how long the form was open; very fast submissions are treated as bots.
 */
(function () {
  var form = document.getElementById("contact-form");
  if (!form) return;

  var loadedAt = Date.now();
  var status = form.querySelector(".status");
  var button = form.querySelector('button[type="submit"]');
  var topic = form.elements.topic;

  function show(kind, text) {
    status.hidden = false;
    status.className = "status " + kind;
    status.textContent = text;
  }

  var FALLBACK = "Something went wrong sending that. Please email hello@leecrowesoftware.com instead.";

  // Result of a no-JavaScript submission, passed back by the API's redirect.
  try {
    var sent = new URLSearchParams(location.search).get("sent");
    if (sent === "1") show("ok", "Thanks, your message is on its way. I'll reply by email.");
    else if (sent === "0") show("err", FALLBACK);
    if (sent !== null) history.replaceState(null, "", location.pathname + "#contact");
  } catch (e) { /* old browser: nothing to show */ }

  // "Discuss a project →" style links preselect the matching topic.
  document.querySelectorAll("a[data-topic]").forEach(function (a) {
    a.addEventListener("click", function () {
      if (topic) topic.value = a.getAttribute("data-topic");
    });
  });

  function firstProblem() {
    var name = form.elements.name.value.trim();
    var email = form.elements.email.value.trim();
    var message = form.elements.message.value.trim();
    if (!name) return [form.elements.name, "Please enter your name."];
    if (!email) return [form.elements.email, "Please enter your email address."];
    if (!form.elements.email.checkValidity()) return [form.elements.email, "That email address doesn't look right."];
    if (message.length < 10) return [form.elements.message, "Please tell me a little more in your message."];
    return null;
  }

  form.addEventListener("submit", function (ev) {
    ev.preventDefault();
    var problem = firstProblem();
    if (problem) {
      show("err", problem[1]);
      problem[0].focus();
      return;
    }

    form.elements.elapsed.value = String(Date.now() - loadedAt);
    var body = new URLSearchParams(new FormData(form));

    button.disabled = true;
    button.textContent = "Sending…";
    status.hidden = true;

    fetch(form.action, {
      method: "POST",
      headers: { Accept: "application/json" },
      body: body
    })
      .then(function (res) {
        return res.json().catch(function () { return { ok: false }; });
      })
      .then(function (data) {
        if (data && data.ok) {
          form.reset();
          show("ok", "Thanks, your message is on its way. I'll reply by email.");
        } else {
          show("err", (data && data.error) || FALLBACK);
        }
      })
      .catch(function () { show("err", FALLBACK); })
      .then(function () {
        button.disabled = false;
        button.textContent = "Send message";
      });
  });
})();
