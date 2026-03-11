const apiUrlInput = document.getElementById('api-url');
const saveBtn = document.getElementById('save');
const statusEl = document.getElementById('status');

chrome.storage.sync.get(['apiUrl'], (result) => {
  apiUrlInput.value = result.apiUrl || 'http://localhost:5000';
});

saveBtn.addEventListener('click', () => {
  chrome.storage.sync.set({ apiUrl: apiUrlInput.value }, () => {
    statusEl.hidden = false;
    setTimeout(() => { statusEl.hidden = true; }, 2000);
  });
});
