/**
 * Extracts location data from TripAdvisor pages.
 * Matches: tripadvisor.com
 * @returns {{ name?: string, address?: string, category?: string } | null}
 */
function parseTripAdvisor() {
  if (!window.location.hostname.includes('tripadvisor.com')) return null;

  const result = {};

  // Name from h1
  const nameEl = document.querySelector('h1[data-test-target="top-info-header"], h1#HEADING');
  if (nameEl) result.name = nameEl.textContent.trim();

  // Address
  const addressEl = document.querySelector('span.fHvkI, button[data-automation="open-map"] span');
  if (addressEl) result.address = addressEl.textContent.trim();

  // Category from URL path
  const path = window.location.pathname;
  if (path.includes('/Restaurant')) result.category = 'restaurant';
  else if (path.includes('/Hotel')) result.category = 'hotel';
  else if (path.includes('/Attraction')) result.category = 'attraction';
  else result.category = 'other';

  return result.name ? result : null;
}
