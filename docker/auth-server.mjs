import { createServer } from "node:http";
import { createServer as createTlsServer } from "node:https";
import { createHash } from "node:crypto";
import { readFile, stat } from "node:fs/promises";
import { join, extname } from "node:path";

const PORT = parseInt(process.env.PORT || "80");
const TLS_PORT = parseInt(process.env.TLS_PORT || "443");
const TLS_CERT = process.env.TLS_CERT || "/app/certs/cert.pem";
const TLS_KEY = process.env.TLS_KEY || "/app/certs/key.pem";
const PROXY_UPSTREAM = process.env.PROXY_UPSTREAM || "http://proxy:8080";
const STATIC_DIR = process.env.STATIC_DIR || "/app/dist";
const COOKIE_NAME = "swa_auth";

const MIME = {
  ".html": "text/html",
  ".js": "application/javascript",
  ".css": "text/css",
  ".json": "application/json",
  ".png": "image/png",
  ".jpg": "image/jpeg",
  ".svg": "image/svg+xml",
  ".ico": "image/x-icon",
  ".woff": "font/woff",
  ".woff2": "font/woff2",
  ".ttf": "font/ttf",
  ".txt": "text/plain",
};

// ---------------------------------------------------------------------------
// Helpers
// ---------------------------------------------------------------------------

import { request as httpRequest } from "node:http";

function parseCookies(header) {
  const out = {};
  (header || "").split(";").forEach((c) => {
    const [k, ...v] = c.split("=");
    if (k) out[k.trim()] = v.join("=").trim();
  });
  return out;
}

function getUserFromCookie(req) {
  const val = parseCookies(req.headers.cookie)[COOKIE_NAME];
  if (!val) return null;
  try {
    return JSON.parse(
      Buffer.from(decodeURIComponent(val), "base64").toString()
    );
  } catch {
    return null;
  }
}

/** Deterministic short id from email so the same email always gets the same proxy API key. */
function makeUserId(email) {
  return createHash("sha256")
    .update(email.toLowerCase().trim())
    .digest("hex")
    .slice(0, 16);
}

function makePrincipal(user) {
  return {
    identityProvider: "email",
    userId: user.userId,
    userDetails: user.email,
    userRoles: ["anonymous", "authenticated"],
  };
}

function makePrincipalB64(user) {
  return Buffer.from(JSON.stringify(makePrincipal(user))).toString("base64");
}

function readBody(req, limit = 2048) {
  return new Promise((resolve, reject) => {
    let len = 0;
    const chunks = [];
    req.on("data", (c) => {
      len += c.length;
      if (len > limit) {
        req.destroy();
        reject(new Error("Body too large"));
      }
      chunks.push(c);
    });
    req.on("end", () => resolve(Buffer.concat(chunks).toString()));
    req.on("error", reject);
  });
}

// ---------------------------------------------------------------------------
// Login page
// ---------------------------------------------------------------------------

function loginPageHtml(redirectUri) {
  const safe = redirectUri
    .replace(/&/g, "&amp;")
    .replace(/"/g, "&quot;")
    .replace(/</g, "&lt;");
  return `<!DOCTYPE html>
<html lang="en"><head>
<meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1">
<title>Sign in</title>
<style>
  * { box-sizing: border-box; }
  body { font-family: system-ui, -apple-system, sans-serif; display: flex;
         justify-content: center; align-items: center; min-height: 100vh;
         margin: 0; background: #f5f5f5; }
  .card { background: #fff; padding: 2rem; border-radius: 8px;
          box-shadow: 0 2px 12px rgba(0,0,0,.1); width: 100%; max-width: 400px; }
  h2 { margin-top: 0; }
  label { display: block; margin-bottom: .25rem; font-weight: 500; }
  input[type=email] { width: 100%; padding: .6rem; font-size: 1rem;
                       border: 1px solid #ccc; border-radius: 4px; }
  button { margin-top: 1rem; width: 100%; padding: .6rem; font-size: 1rem;
           background: #0078d4; color: #fff; border: none; border-radius: 4px;
           cursor: pointer; }
  button:hover { background: #106ebe; }
</style>
</head><body>
<div class="card">
  <h2>Sign in</h2>
  <p>Enter your email to register for the event:</p>
  <form method="POST" action="/.auth/login">
    <input type="hidden" name="redirect" value="${safe}">
    <label for="email">Email</label>
    <input id="email" type="email" name="email" required placeholder="you@example.com" autofocus>
    <button type="submit">Continue</button>
  </form>
</div>
</body></html>`;
}

// ---------------------------------------------------------------------------
// Auth handlers
// ---------------------------------------------------------------------------

async function handleAuth(req, res, url) {
  // GET /.auth/me
  if (url.pathname === "/.auth/me") {
    const user = getUserFromCookie(req);
    res.writeHead(200, { "Content-Type": "application/json" });
    res.end(
      JSON.stringify({
        clientPrincipal: user ? makePrincipal(user) : null,
      })
    );
    return;
  }

  // GET /.auth/login/* — render the email form
  if (url.pathname.startsWith("/.auth/login") && req.method === "GET") {
    const redirect =
      url.searchParams.get("post_login_redirect_uri") || "/";
    res.writeHead(200, { "Content-Type": "text/html" });
    res.end(loginPageHtml(redirect));
    return;
  }

  // POST /.auth/login — set cookie and redirect
  if (url.pathname === "/.auth/login" && req.method === "POST") {
    let body;
    try {
      body = await readBody(req);
    } catch {
      res.writeHead(413);
      res.end("Payload too large");
      return;
    }
    const params = new URLSearchParams(body);
    const email = (params.get("email") || "").trim();
    const redirect = params.get("redirect") || "/";

    if (!email) {
      res.writeHead(400, { "Content-Type": "text/plain" });
      res.end("Email is required");
      return;
    }

    const user = { email, userId: makeUserId(email) };
    const cookieVal = Buffer.from(JSON.stringify(user)).toString("base64");
    console.log(`Login: ${email} → userId ${user.userId}`);
    const secureSuffix = req.socket.encrypted ? "; Secure" : "";
    res.writeHead(302, {
      "Set-Cookie": `${COOKIE_NAME}=${encodeURIComponent(cookieVal)}; Path=/; HttpOnly; SameSite=Lax${secureSuffix}`,
      Location: redirect,
    });
    res.end();
    return;
  }

  // GET /.auth/logout
  if (url.pathname === "/.auth/logout") {
    const redirect =
      url.searchParams.get("post_logout_redirect_uri") || "/";
    const secureSuffix = req.socket.encrypted ? "; Secure" : "";
    res.writeHead(302, {
      "Set-Cookie": `${COOKIE_NAME}=; Path=/; HttpOnly; SameSite=Lax; Max-Age=0${secureSuffix}`,
      Location: redirect,
    });
    res.end();
    return;
  }

  res.writeHead(404);
  res.end("Not found");
}

// ---------------------------------------------------------------------------
// Reverse proxy  /api/* → backend
// ---------------------------------------------------------------------------

function proxyRequest(req, res) {
  const target = new URL(req.url, PROXY_UPSTREAM);
  const user = getUserFromCookie(req);

  // Forward all headers except cookie (don't leak auth cookie to backend)
  const headers = { ...req.headers, host: target.host };
  delete headers["cookie"];

  if (user) {
    headers["x-ms-client-principal"] = makePrincipalB64(user);
  }

  const proxyReq = httpRequest(
    target,
    { method: req.method, headers },
    (proxyRes) => {
      res.writeHead(proxyRes.statusCode, proxyRes.headers);
      proxyRes.pipe(res, { end: true });
    }
  );

  proxyReq.on("error", (err) => {
    console.error("Proxy error:", err.message);
    if (!res.headersSent) {
      res.writeHead(502, { "Content-Type": "text/plain" });
      res.end("Bad Gateway");
    }
  });

  req.pipe(proxyReq, { end: true });
}

// ---------------------------------------------------------------------------
// Static file server (SPA fallback)
// ---------------------------------------------------------------------------

async function serveStatic(res, url) {
  let filePath = join(STATIC_DIR, url.pathname);

  try {
    const s = await stat(filePath);
    if (s.isDirectory()) filePath = join(filePath, "index.html");
  } catch {
    // SPA client-side routing fallback
    filePath = join(STATIC_DIR, "index.html");
  }

  try {
    const content = await readFile(filePath);
    const ext = extname(filePath);
    res.writeHead(200, {
      "Content-Type": MIME[ext] || "application/octet-stream",
    });
    res.end(content);
  } catch {
    res.writeHead(404, { "Content-Type": "text/plain" });
    res.end("Not found");
  }
}

// ---------------------------------------------------------------------------
// Main
// ---------------------------------------------------------------------------

const handler = async (req, res) => {
  const proto = req.socket.encrypted ? "https" : "http";
  const url = new URL(req.url, `${proto}://${req.headers.host}`);

  if (url.pathname.startsWith("/.auth")) return handleAuth(req, res, url);
  if (url.pathname.startsWith("/api/")) return proxyRequest(req, res);
  return serveStatic(res, url);
};

// Always start HTTP
createServer(handler).listen(PORT, () => {
  console.log(`Registration server (HTTP) on :${PORT}`);
  console.log(`Proxying /api/ → ${PROXY_UPSTREAM}`);
});

// Start HTTPS if certs exist
try {
  const [cert, key] = await Promise.all([
    readFile(TLS_CERT),
    readFile(TLS_KEY),
  ]);
  createTlsServer({ cert, key }, handler).listen(TLS_PORT, () => {
    console.log(`Registration server (HTTPS) on :${TLS_PORT}`);
  });
} catch {
  console.log("No TLS certs found — HTTPS disabled");
}
