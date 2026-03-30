const DEFAULT_API_URL = 'http://localhost:5197';

async function getApiUrl() {
  const result = await chrome.storage.sync.get(['apiUrl']);
  return result.apiUrl || DEFAULT_API_URL;
}

async function saveLocation(locationData) {
  const apiUrl = await getApiUrl();
  const response = await fetch(`${apiUrl}/api/locations`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify(locationData)
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: 'Unknown error' }));
    throw new Error(error.error || `API returned ${response.status}`);
  }

  return await response.json();
}

async function assignLocation(locationId, tripId) {
  const apiUrl = await getApiUrl();
  const response = await fetch(`${apiUrl}/api/locations/${locationId}/assign`, {
    method: 'POST',
    headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ tripId })
  });

  if (!response.ok) {
    const error = await response.json().catch(() => ({ error: 'Unknown error' }));
    throw new Error(error.error || `API returned ${response.status}`);
  }

  return await response.json();
}

async function getTrips() {
  const apiUrl = await getApiUrl();
  const response = await fetch(`${apiUrl}/api/trip`);
  if (!response.ok) throw new Error(`Failed to fetch trips: ${response.status}`);
  return await response.json();
}

// Handle messages from popup
chrome.runtime.onMessage.addListener((message, sender, sendResponse) => {
  if (message.action === 'saveLocation') {
    saveLocation(message.data)
      .then(result => sendResponse({ success: true, data: result }))
      .catch(err => sendResponse({ success: false, error: err.message }));
    return true;
  }

  if (message.action === 'extractViaLlm') {
    // Return the raw data for preview only — don't save yet.
    // The user will click "Save to Ideas" to trigger the actual save.
    sendResponse({ success: true, data: message.data });
    return false;
  }

  if (message.action === 'saveAndAssign') {
    saveLocation(message.data)
      .then(saved => assignLocation(saved.id, message.tripId))
      .then(result => sendResponse({ success: true, data: result }))
      .catch(err => sendResponse({ success: false, error: err.message }));
    return true;
  }

  if (message.action === 'getTrips') {
    getTrips()
      .then(trips => sendResponse({ success: true, data: trips }))
      .catch(err => sendResponse({ success: false, error: err.message }));
    return true;
  }
});
