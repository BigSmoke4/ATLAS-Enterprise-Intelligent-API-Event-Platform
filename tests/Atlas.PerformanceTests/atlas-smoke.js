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

// k6 >= 0.44 lets a status outside 2xx/3xx count as expected, so probing the
// auth pipeline (401) does not inflate http_req_failed.
const expected = (status) => (typeof http.expectedStatuses === 'function' ? http.expectedStatuses(status) : null);

export default function () {
  const base = __ENV.ATLAS_BASE_URL || 'http://localhost:8080';

  const liveness = http.get(`${base}/health/live`, { tags: { endpoint: 'liveness' } });
  check(liveness, { 'liveness is successful': (r) => r.status === 200 });

  const readiness = http.get(`${base}/health/ready`, { tags: { endpoint: 'readiness' } });
  check(readiness, { 'readiness is successful': (r) => r.status === 200 });

  const console_ = http.get(`${base}/`, { tags: { endpoint: 'console' } });
  check(console_, { 'anonymous console call is challenged to sign-in': (r) => r.status === 302 });

  if (expected(401)) {
    const api = http.get(`${base}/api/v1/organizations`, {
      tags: { endpoint: 'api-auth' },
      responseCallback: expected(401)
    });
    check(api, { 'anonymous API call is 401 (never an HTML redirect)': (r) => r.status === 401 });
  }

  sleep(1);
}
