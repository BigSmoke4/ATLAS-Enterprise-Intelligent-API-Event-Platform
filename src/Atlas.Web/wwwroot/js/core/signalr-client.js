export function connectIncidentHub(organizationId, onUpdate) {
  if (!window.signalR) return { stop: async () => {}, connected: false };
  const connection = new window.signalR.HubConnectionBuilder().withUrl('/hubs/incidents').withAutomaticReconnect().build();
  connection.on('incident-updated', onUpdate);
  connection.start().then(() => connection.invoke('JoinOrganization', organizationId)).catch(() => {});
  return { stop: () => connection.stop(), connected: true };
}
