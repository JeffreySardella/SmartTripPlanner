const loadingEl = document.getElementById('loading');
const loadingText = document.getElementById('loading-text');
const locationInfoEl = document.getElementById('location-info');
const noLocationEl = document.getElementById('no-location');
const statusEl = document.getElementById('status');
const statusText = document.getElementById('status-text');
const errorEl = document.getElementById('error');
const errorText = document.getElementById('error-text');

const locationNameEl = document.getElementById('location-name');
const locationAddressEl = document.getElementById('location-address');
const locationCategoryEl = document.getElementById('location-category');
const saveIdeasBtn = document.getElementById('save-ideas');
const tripSelect = document.getElementById('trip-select');
const saveTripBtn = document.getElementById('save-trip');

let currentLocationData = null;

async function init() {
  try {
    // Content scripts are injected by manifest.json at document_idle.
    // Query the active tab and ask the content script to extract location.
    const [tab] = await chrome.tabs.query({ active: true, currentWindow: true });
    const response = await chrome.tabs.sendMessage(tab.id, { action: 'extractLocation' });

    if (response.success) {
      showLocation(response.data, response.sourceUrl || tab.url);
    } else if (response.needsLlm) {
      loadingText.textContent = 'Analyzing page...';
      await handleLlmFallback(response.rawPageContent, response.sourceUrl || tab.url);
    } else {
      showNoLocation();
    }
  } catch (err) {
    showError('Failed to scan page: ' + err.message);
  }

  // Load trips for dropdown
  loadTrips();
}

function showLocation(data, sourceUrl) {
  currentLocationData = { ...data, sourceUrl };
  loadingEl.hidden = true;
  locationInfoEl.hidden = false;

  locationNameEl.textContent = data.name || 'Unknown';
  locationAddressEl.textContent = data.address || '';
  locationCategoryEl.textContent = data.category
    ? data.category.charAt(0).toUpperCase() + data.category.slice(1)
    : '';

  locationAddressEl.hidden = !data.address;
  locationCategoryEl.hidden = !data.category;
}

function showNoLocation() {
  loadingEl.hidden = true;
  noLocationEl.hidden = false;
}

function showStatus(msg) {
  statusEl.hidden = false;
  statusText.textContent = msg;
  setTimeout(() => window.close(), 2000);
}

function showError(msg) {
  loadingEl.hidden = true;
  errorEl.hidden = false;
  errorText.textContent = msg;
}

async function handleLlmFallback(rawPageContent, sourceUrl) {
  // Send to API for LLM extraction only — show results for user to review
  const response = await chrome.runtime.sendMessage({
    action: 'extractViaLlm',
    data: { rawPageContent, sourceUrl }
  });

  if (response.success) {
    showLocation(response.data, sourceUrl);
  } else {
    showNoLocation();
    showError(response.error || 'Could not extract location');
  }
}

async function loadTrips() {
  let response;
  try {
    response = await chrome.runtime.sendMessage({ action: 'getTrips' });
  } catch {
    return;
  }
  if (!response || !response.success) return;

  const trips = response.data.filter(t => t.status !== 'completed');
  for (const trip of trips) {
    const option = document.createElement('option');
    option.value = trip.id;
    option.textContent = trip.destination;
    tripSelect.appendChild(option);
  }
}

tripSelect.addEventListener('change', () => {
  saveTripBtn.disabled = !tripSelect.value;
});

saveIdeasBtn.addEventListener('click', async () => {
  if (!currentLocationData) return;
  saveIdeasBtn.disabled = true;

  const response = await chrome.runtime.sendMessage({
    action: 'saveLocation',
    data: currentLocationData
  });

  if (response.success) {
    showStatus('Saved to Ideas!');
  } else {
    showError(response.error || 'Failed to save');
    saveIdeasBtn.disabled = false;
  }
});

saveTripBtn.addEventListener('click', async () => {
  if (!currentLocationData || !tripSelect.value) return;
  saveTripBtn.disabled = true;

  const response = await chrome.runtime.sendMessage({
    action: 'saveAndAssign',
    data: currentLocationData,
    tripId: parseInt(tripSelect.value)
  });

  if (response.success) {
    showStatus('Added to trip!');
  } else {
    showError(response.error || 'Failed to save');
    saveTripBtn.disabled = false;
  }
});

init();
