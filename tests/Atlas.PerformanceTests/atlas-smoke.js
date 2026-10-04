// k6 smoke scenario, run by CI against a real ATLAS host (see .github/workflows/ci.yml)
// and runnable by hand against any environment:
//
//   ATLAS_BASE_URL=https://atlas.example.com VUS=4 DURATION=60s k6 run \
//       tests/Atlas.PerformanceTests/atlas-smoke.js
//
// This is a smoke gate, not a benchmark: it asserts the documented contracts
// (liveness 200, dependency-aware readiness 200, the console challenges an
// anonymous browser, and an anonymous API call is 401 rather than an HTML
// redirect) and prints the measured latencies in its summary. Reported numbers
// always come from the environment the script actually hit — nothing here is
// fabricated into source control.
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  vus: Number(__ENV.VUS || 2),
  duration: __ENV.DURATION || '20s',
  thresholds: {
    // A smoke run must not tolerate failed contracts; a real failure budget
    // belongs to an SLO, not to this script.
    http_req_failed: ['rate<0.01'],
    checks: ['rate==1'],
    // Tagged so only the trivial liveness endpoint is held to the platform's
    // API latency target; readiness deliberately touches Postgres, Redis and
    // Kafka and is not given a budget it cannot keep.
    'http_req_duration{endpoint:liveness}': ['p(95)<250']
  }
};

// The status codes this gate probes on purpose. Registering them as expected
// keeps `http_req_failed` about *unexpected* responses (a 500, a truncated
// connection) while each check below still asserts the exact status it must
// observe. k6 >= 0.44 exposes http.expectedStatuses; older builds fall back to
// the default 2xx/3xx rule, where the deliberate 401 shows up in
// http_req_failed — the per-check assertion is the contract either way.
const expectedStatuses = typeof http.expectedStatuses === 'function'
  ? http.expectedStatuses(200, 302, 401)
  : null;

function probe(endpoint, extra = {}) {
  const params = { tags: { endpoint }, ...extra };
  if (expectedStatuses) params.responseCallback = expectedStatuses;
  return params;
}

export default function () {
  const base = __ENV.ATLAS_BASE_URL || 'http://localhost:8080';

  const liveness = http.get(`${base}/health/live`, probe('liveness'));
  check(liveness, { 'liveness is successful': (r) => r.status === 200 });

  const readiness = http.get(`${base}/health/ready`, probe('readiness'));
  check(readiness, { 'readiness is successful': (r) => r.status === 200 });

  // redirects: 0 — the contract is that the console *challenges* an anonymous
  // browser. k6 follows redirects by default, which would report the sign-in
  // page's 200 instead of the 302 that proves the challenge happened.
  const consoleCall = http.get(`${base}/`, probe('console', { redirects: 0 }));
  check(consoleCall, { 'anonymous console call is challenged to sign-in': (r) => r.status === 302 });

  const api = http.get(`${base}/api/v1/organizations`, probe('api-auth'));
  check(api, { 'anonymous API call is 401 (never an HTML redirect)': (r) => r.status === 401 });

  sleep(1);
}
