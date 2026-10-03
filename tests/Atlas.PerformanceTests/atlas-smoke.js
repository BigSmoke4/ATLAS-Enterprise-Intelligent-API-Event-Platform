// k6 smoke scenario. This intentionally reports measurements from the target
// environment; it does not publish benchmark claims in source control.
import http from 'k6/http';
import { check, sleep } from 'k6';

export const options = {
  vus: Number(__ENV.VUS || 1),
  duration: __ENV.DURATION || '30s',
  thresholds: { http_req_failed: ['rate<0.01'] }
};

export default function () {
  const base = __ENV.ATLAS_BASE_URL || 'http://localhost:8080';
  const response = http.get(`${base}/health/live`);
  check(response, { 'liveness is successful': r => r.status === 200 });
  sleep(1);
}
