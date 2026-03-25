const apiUrlInput = document.getElementById('api-url');
const saveBtn = document.getElementById('save');
const statusEl = document.getElementById('status');

chrome.storage.sync.get(['apiUrl'], (result) => {
  apiUrlInput.value = result.apiUrl || 'http://localhost:5197';
});

saveBtn.addEventListener('click', () => {
  const value = apiUrlInput.value.trim();
  try {
    const parsed = new URL(value);
    if (parsed.protocol !== 'http:' && parsed.protocol !== 'https:') {
      throw new Error('Invalid protocol');
    }
  } catch {
    statusEl.textContent = 'Invalid URL — must start with http:// or https://';
    statusEl.className = 'error';
    statusEl.hidden = false;
    setTimeout(() => { statusEl.hidden = true; }, 3000);
    return;
  }

  chrome.storage.sync.set({ apiUrl: value }, () => {
    statusEl.textContent = 'Saved!';
    statusEl.className = 'saved';
    statusEl.hidden = false;
    setTimeout(() => { statusEl.hidden = true; }, 2000);
  });
});
